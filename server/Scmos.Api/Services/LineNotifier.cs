using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Scmos.Api.Services;

/// <summary>
/// The one way SCMOS speaks into a LINE group: a push of plain text.
///
/// <para>
/// Everything else about LINE is inbound. This exists for the morning
/// reminder, and is kept to text on purpose — a message a haulier reads on a
/// phone at a yard gate, not a card. It never says anything a person did
/// not ask it to say: the caller composes, this delivers.
/// </para>
///
/// <para>
/// The channel access token is read from configuration at the moment of
/// sending and goes into one header. It is never logged, echoed or stored.
/// </para>
/// </summary>
public interface ILineNotifier
{
    /// <summary>Whether a message can be sent at all: the integration is on and the token is set.</summary>
    bool Configured { get; }

    /// <summary>Why not, when it cannot.</summary>
    string Missing { get; }

    /// <summary>Sends up to five texts to one group. Returns the failure in words, or empty.</summary>
    Task<string> PushAsync(string lineGroupId, IReadOnlyList<string> texts, CancellationToken token);

    /// <summary>
    /// The month's push allowance and how much of it is used, as LINE
    /// reports them — or what stopped the asking. A plan with no cap reports
    /// no limit. The one figure that explains a whole afternoon of pushes
    /// going nowhere (17 Sep 2026).
    /// </summary>
    Task<LineNotifier.Quota> QuotaAsync(CancellationToken token);

    /// <summary>
    /// Answers the message that carried this reply token — LINE's free
    /// channel, good for about a minute after the message. When the token
    /// has expired and a group is given, the text is pushed instead.
    /// Returns the failure in words, or empty.
    /// </summary>
    Task<string> ReplyAsync(string replyToken, string lineGroupId, IReadOnlyList<string> texts, CancellationToken token);
}

public class LineNotifier(IHttpClientFactory factory, IConfiguration config, ILogger<LineNotifier> log) : ILineNotifier
{
    /// <summary>The Messaging API token, which is what pushing and replying need. Never logged, never echoed.</summary>
    public const string TokenKey = "Line:ChannelAccessToken";

    /// <summary>The named HttpClient every call to LINE goes through.</summary>
    public const string ClientName = "line";

    private const string PushUrl = "https://api.line.me/v2/bot/message/push";
    private const string QuotaUrl = "https://api.line.me/v2/bot/message/quota";
    private const string ConsumptionUrl = "https://api.line.me/v2/bot/message/quota/consumption";

    /// <param name="Limit">The month's cap, or null when the plan has none.</param>
    /// <param name="Used">Messages sent this month, or null when LINE would not say.</param>
    /// <param name="Problem">Why the figures are missing, in words, or empty.</param>
    public record Quota(int? Limit, long? Used, string Problem)
    {
        /// <summary>Whether the cap is reached — every push from here on is refused until the month turns.</summary>
        public bool Exhausted => Limit is { } cap && Used is { } used && used >= cap;
    }

    public async Task<Quota> QuotaAsync(CancellationToken token)
    {
        if (!Configured) return new Quota(null, null, Missing);
        try
        {
            var client = factory.CreateClient(ClientName);
            client.Timeout = TimeSpan.FromSeconds(15);
            int? limit = null;
            long? used = null;
            foreach (var (url, which) in new[] { (QuotaUrl, "quota"), (ConsumptionUrl, "consumption") })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config[TokenKey]);
                using var response = await client.SendAsync(request, token);
                var answer = await response.Content.ReadAsStringAsync(token);
                if (!response.IsSuccessStatusCode) return new Quota(null, null, $"LINE ตอบ {(int)response.StatusCode} ตอนถามโควตา: {Detail(answer)}");
                using var json = JsonDocument.Parse(answer);
                if (which == "quota")
                {
                    // {"type":"limited","value":200} or {"type":"none"}
                    if (json.RootElement.TryGetProperty("type", out var type) && type.GetString() == "limited"
                        && json.RootElement.TryGetProperty("value", out var value) && value.TryGetInt32(out var cap))
                        limit = cap;
                }
                else if (json.RootElement.TryGetProperty("totalUsage", out var total) && total.TryGetInt64(out var sent))
                {
                    used = sent;
                }
            }
            return new Quota(limit, used, "");
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return new Quota(null, null, $"ถามโควตาไม่ได้: {error.GetType().Name}");
        }
    }
    private const string ReplyUrl = "https://api.line.me/v2/bot/message/reply";

    public bool Configured => Missing.Length == 0;

    public string Missing =>
        !config.GetValue("Line:Enabled", false) ? "ปิดอยู่ — ตั้ง Line__Enabled เป็น true เมื่อพร้อม"
        : string.IsNullOrWhiteSpace(config[TokenKey]) ? "ยังไม่ได้ตั้ง Line__ChannelAccessToken"
        : "";

    public async Task<string> PushAsync(string lineGroupId, IReadOnlyList<string> texts, CancellationToken token)
    {
        if (!Configured) return Missing;
        if (string.IsNullOrWhiteSpace(lineGroupId)) return "ไม่มีรหัสกลุ่ม";
        if (texts.Count == 0) return "ไม่มีข้อความ";
        if (texts.Count > 5) return "ส่งได้ครั้งละไม่เกิน 5 ข้อความ";

        var body = JsonSerializer.Serialize(new
        {
            to = lineGroupId,
            messages = texts.Select(text => new { type = "text", text }),
        });
        var (ok, status, detail) = await SendAsync(PushUrl, body, token);
        if (ok)
        {
            log.LogInformation("LINE push to {Group}: {Count} message(s)", lineGroupId, texts.Count);
            return "";
        }

        // LINE's answer names the problem — a bot no longer in the group, a
        // bad token, a message too long — and that is what a person needs.
        log.LogWarning("LINE push to {Group} refused: {Status} {Detail}", lineGroupId, status, detail);
        return status switch
        {
            401 => "LINE ไม่รับ Line__ChannelAccessToken — ตรวจค่าใน Portal",
            400 when detail.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || detail.Contains("invalid", StringComparison.OrdinalIgnoreCase)
                => $"LINE ไม่รู้จักกลุ่มนี้ หรือ bot ไม่ได้อยู่ในกลุ่มแล้ว ({detail})",
            _ => $"LINE ตอบ {status}: {detail}",
        };
    }

    public async Task<string> ReplyAsync(string replyToken, string lineGroupId, IReadOnlyList<string> texts, CancellationToken token)
    {
        if (!Configured) return Missing;
        if (texts.Count == 0) return "ไม่มีข้อความ";
        if (!string.IsNullOrWhiteSpace(replyToken))
        {
            var body = JsonSerializer.Serialize(new
            {
                replyToken,
                messages = texts.Select(text => new { type = "text", text }),
            });
            var (ok, status, detail) = await SendAsync(ReplyUrl, body, token);
            if (ok) return "";
            // An expired or used token is the ordinary way a reply fails —
            // the worker got to the message late. The push behind it costs a
            // message from the account's monthly allowance, which is why the
            // reply is tried first.
            log.LogInformation("LINE reply refused ({Status} {Detail}); pushing instead", status, detail);
        }
        return await PushAsync(lineGroupId, texts, token);
    }

    private async Task<(bool Ok, int Status, string Detail)> SendAsync(string url, string body, CancellationToken token)
    {
        var client = factory.CreateClient(ClientName);
        client.Timeout = TimeSpan.FromSeconds(30);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config[TokenKey]);
        using var response = await client.SendAsync(request, token);
        if (response.IsSuccessStatusCode) return (true, (int)response.StatusCode, "");
        var answer = await response.Content.ReadAsStringAsync(token);
        return (false, (int)response.StatusCode, Detail(answer));
    }

    /// <summary>The message field of LINE's error body, or the body itself, kept short.</summary>
    private static string Detail(string answer)
    {
        try
        {
            using var json = JsonDocument.Parse(answer);
            if (json.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                return message.GetString() ?? "";
        }
        catch (JsonException) { }
        return answer.Length > 200 ? answer[..200] : answer;
    }
}
