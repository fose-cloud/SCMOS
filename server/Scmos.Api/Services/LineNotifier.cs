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
}

public class LineNotifier(IHttpClientFactory factory, IConfiguration config, ILogger<LineNotifier> log) : ILineNotifier
{
    private const string PushUrl = "https://api.line.me/v2/bot/message/push";

    public bool Configured => Missing.Length == 0;

    public string Missing =>
        !config.GetValue("Line:Enabled", false) ? "ปิดอยู่ — ตั้ง Line__Enabled เป็น true เมื่อพร้อม"
        : string.IsNullOrWhiteSpace(config[LineImageReader.TokenKey]) ? "ยังไม่ได้ตั้ง Line__ChannelAccessToken"
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

        var client = factory.CreateClient(LineImageReader.ClientName);
        client.Timeout = TimeSpan.FromSeconds(30);
        using var request = new HttpRequestMessage(HttpMethod.Post, PushUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config[LineImageReader.TokenKey]);

        using var response = await client.SendAsync(request, token);
        if (response.IsSuccessStatusCode)
        {
            log.LogInformation("LINE push to {Group}: {Count} message(s)", lineGroupId, texts.Count);
            return "";
        }

        // LINE's answer names the problem — a bot no longer in the group, a
        // bad token, a message too long — and that is what a person needs.
        var answer = await response.Content.ReadAsStringAsync(token);
        var detail = Detail(answer);
        log.LogWarning("LINE push to {Group} refused: {Status} {Detail}", lineGroupId, (int)response.StatusCode, detail);
        return response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => "LINE ไม่รับ Line__ChannelAccessToken — ตรวจค่าใน Portal",
            System.Net.HttpStatusCode.BadRequest when detail.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || detail.Contains("invalid", StringComparison.OrdinalIgnoreCase)
                => $"LINE ไม่รู้จักกลุ่มนี้ หรือ bot ไม่ได้อยู่ในกลุ่มแล้ว ({detail})",
            _ => $"LINE ตอบ {(int)response.StatusCode}: {detail}",
        };
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
