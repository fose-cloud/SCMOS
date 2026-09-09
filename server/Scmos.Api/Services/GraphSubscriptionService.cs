using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// Creating, renewing and deleting the Graph subscriptions that make mail
/// arrive at all.
///
/// <para>
/// <b>The failure this guards against is silence.</b> Graph expires a mail
/// subscription in under three days and stops delivering without saying so, and
/// a mailbox that has quietly stopped being read looks exactly like a mailbox
/// nobody has written to. Everything here exists so that state is written down
/// and acted on rather than assumed.
/// </para>
///
/// <para>
/// <b>Creating a subscription cannot succeed until the webhook exists.</b>
/// Graph calls the notification URL during the create and expects the
/// <c>validationToken</c> echoed within ten seconds; until step 7 answers that,
/// every create fails the handshake. That is expected, and it is why the
/// failure is reported as a sentence about the handshake rather than as a
/// generic 400.
/// </para>
/// </summary>
public sealed class GraphSubscriptionService(
    GraphAuth graph, ScmosDbContext db, IConfiguration config, ILogger<GraphSubscriptionService> log)
{
    /// <summary>Where the webhook lives, so a subscription can be told where to deliver.</summary>
    public const string WebhookBaseKey = "Graph:WebhookBase";

    /// <summary>
    /// The two paths, ours to define and step 7's to answer.
    ///
    /// Constants rather than two more app settings: one base URL is one thing
    /// to get right, and a path typed into a portal is a path that can be typed
    /// wrongly into a portal.
    /// </summary>
    public const string NotifyPath = "/api/integrations/graph/notify";
    public const string LifecyclePath = "/api/integrations/graph/lifecycle";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(45);

    /// <summary>What happened, and the row it happened to.</summary>
    public record Outcome(GraphDiagnosis.Finding Finding, GraphSubscription? Subscription = null);

    /// <summary>The configured base, or empty when nobody has set one.</summary>
    public string WebhookBase => (config[WebhookBaseKey] ?? "").Trim().TrimEnd('/');

    public string NotifyUrl => WebhookBase.Length == 0 ? "" : WebhookBase + NotifyPath;
    public string LifecycleUrl => WebhookBase.Length == 0 ? "" : WebhookBase + LifecyclePath;

    /// <summary>
    /// Whether a subscription could be created at all, before trying.
    ///
    /// Separate from the attempt so the loop can stay quiet rather than failing
    /// hourly against a setting nobody has filled in yet.
    /// </summary>
    public GraphDiagnosis.Finding? WhyNotReady() =>
        WebhookBase.Length == 0
            ? new(GraphDiagnosis.Code.NotApproved,
                $"ยังไม่ได้ตั้ง Graph__WebhookBase — Graph ยังไม่รู้ว่าจะส่งแจ้งเตือนมาที่ไหน", false)
        : !GraphSubscriptions.IsDeliverable(NotifyUrl)
            ? new(GraphDiagnosis.Code.NotApproved,
                $"Graph__WebhookBase ({WebhookBase}) เป็นที่อยู่ที่ Microsoft เรียกจากภายนอกไม่ได้ — "
                + "ต้องเป็น https และเป็นชื่อที่เข้าถึงได้จากอินเทอร์เน็ต", false)
        : null;

    /// <summary>
    /// Subscribe to one mailbox, and record what Graph said.
    ///
    /// <para>
    /// Refuses when an active subscription already exists for that mailbox. Two
    /// would mean every notification twice — harmless in the end, because the
    /// unique key on <c>(MailboxId, GraphMessageId)</c> stores the message once
    /// either way, but it doubles the calls and makes the renewal loop's job
    /// ambiguous. Two instances racing could still slip past this check; the
    /// same unique key is what makes that survivable rather than serious.
    /// </para>
    /// </summary>
    public async Task<Outcome> CreateAsync(Mailbox mailbox, CancellationToken token)
    {
        if (!graph.Approves(mailbox.Address)) return new(GraphDiagnosis.NotApproved);
        if (WhyNotReady() is { } why) return new(why);

        var already = await db.GraphSubscriptions
            .Where(one => one.MailboxId == mailbox.Id && one.Status == MailSubscription.Active)
            .FirstOrDefaultAsync(token);
        if (already is not null) return new(GraphDiagnosis.Connected, already);

        var resource = GraphSubscriptions.ResourceFor(mailbox.GraphUserId, mailbox.Address, mailbox.FolderId);
        // Not a Graph 404 — Graph was never asked. A mailbox row with neither
        // an address nor a user id names nothing to watch.
        if (resource.Length == 0)
            return new(new(GraphDiagnosis.Code.NotFound,
                "แถวตู้จดหมายนี้ไม่มีทั้งอีเมลและ Graph user id — ไม่รู้ว่าจะติดตามอะไร", false));

        var now = DateTimeOffset.UtcNow;
        var expiry = GraphSubscriptions.ExpiryFrom(now);
        var secret = GraphSubscriptions.NewClientState();

        var body = JsonSerializer.Serialize(new
        {
            changeType = "created",
            notificationUrl = NotifyUrl,
            lifecycleNotificationUrl = LifecycleUrl,
            resource,
            expirationDateTime = expiry.UtcDateTime,
            clientState = secret,
        });

        var (finding, answer) = await SendAsync(HttpMethod.Post,
            $"{GraphAuth.Endpoint}/subscriptions", body, token);
        if (!finding.Ok) return new(finding);

        var row = new GraphSubscription
        {
            MailboxId = mailbox.Id,
            SubscriptionId = Text(answer, "id"),
            Resource = resource,
            NotificationUrl = NotifyUrl,
            ClientState = secret,
            // Graph's own answer, not what was asked for. It is allowed to give
            // less, and renewing against a time it never agreed to is renewing
            // late.
            ExpiresAt = Moment(answer, "expirationDateTime") ?? expiry,
            Status = MailSubscription.Active,
            CreatedAt = now,
        };

        if (row.SubscriptionId.Length == 0)
        {
            log.LogError("Graph accepted a subscription for {Mailbox} but named no id", mailbox.Address);
            return new(GraphDiagnosis.ForStatus(502, true));
        }

        db.GraphSubscriptions.Add(row);
        await db.SaveChangesAsync(token);
        log.LogInformation("Subscribed to {Mailbox} until {Expires:u}", mailbox.Address, row.ExpiresAt);
        return new(GraphDiagnosis.Connected, row);
    }

    /// <summary>
    /// Push one subscription's expiry out again.
    ///
    /// A 404 means Graph has already forgotten it, which is not an error to
    /// retry — the row is marked expired so the loop creates a new one on its
    /// next pass rather than renewing something that is gone.
    /// </summary>
    public async Task<Outcome> RenewAsync(GraphSubscription row, CancellationToken token)
    {
        var expiry = GraphSubscriptions.ExpiryFrom(DateTimeOffset.UtcNow);
        var body = JsonSerializer.Serialize(new { expirationDateTime = expiry.UtcDateTime });

        var (finding, answer) = await SendAsync(HttpMethod.Patch,
            $"{GraphAuth.Endpoint}/subscriptions/{Uri.EscapeDataString(row.SubscriptionId)}", body, token);

        if (!finding.Ok)
        {
            if (finding.Code == GraphDiagnosis.Code.NotFound)
            {
                row.Status = MailSubscription.Expired;
                await db.SaveChangesAsync(token);
                log.LogWarning("Graph no longer knows subscription {Id}; it will be created again",
                    row.SubscriptionId);
            }
            return new(finding, row);
        }

        row.ExpiresAt = Moment(answer, "expirationDateTime") ?? expiry;
        row.LastRenewedAt = DateTimeOffset.UtcNow;
        row.Status = MailSubscription.Active;
        await db.SaveChangesAsync(token);
        log.LogInformation("Renewed subscription {Id} until {Expires:u}", row.SubscriptionId, row.ExpiresAt);
        return new(GraphDiagnosis.Connected, row);
    }

    /// <summary>
    /// Stop a subscription, and say so in the row.
    ///
    /// The row is marked deleted even when Graph answers 404, because a
    /// subscription Graph has never heard of is a subscription that is stopped.
    /// </summary>
    public async Task<Outcome> DeleteAsync(GraphSubscription row, CancellationToken token)
    {
        var (finding, _) = await SendAsync(HttpMethod.Delete,
            $"{GraphAuth.Endpoint}/subscriptions/{Uri.EscapeDataString(row.SubscriptionId)}", null, token);

        if (!finding.Ok && finding.Code != GraphDiagnosis.Code.NotFound) return new(finding, row);

        row.Status = MailSubscription.Deleted;
        await db.SaveChangesAsync(token);
        log.LogInformation("Stopped subscription {Id}", row.SubscriptionId);
        return new(GraphDiagnosis.Connected, row);
    }

    /// <summary>
    /// Fill in the Graph user id for a mailbox that has none.
    ///
    /// <para>
    /// Worth a call of its own, because it is what stops a renewed subscription
    /// following a reassigned address to whoever holds it next. Returns false
    /// without complaint when it cannot be resolved — the subscription still
    /// works against the address, it just loses that protection, and refusing
    /// to subscribe at all would be a worse answer.
    /// </para>
    /// </summary>
    public async Task<bool> ResolveUserIdAsync(Mailbox mailbox, CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(mailbox.GraphUserId)) return true;
        if (!graph.Approves(mailbox.Address)) return false;

        var (finding, answer) = await SendAsync(HttpMethod.Get,
            $"{GraphAuth.Endpoint}/users/{Uri.EscapeDataString(mailbox.Address)}?$select=id", null, token);
        if (!finding.Ok) return false;

        var id = Text(answer, "id");
        if (id.Length == 0) return false;

        mailbox.GraphUserId = id.Length <= 64 ? id : id[..64];
        mailbox.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);
        return true;
    }

    /// <summary>
    /// One call to the subscriptions API, with the diagnosis attached.
    ///
    /// <para>
    /// The error body <b>is</b> logged here, unlike on the mail routes. A
    /// failure from <c>/subscriptions</c> is about our own URL and our own
    /// request — it cannot quote the contents of anybody's mailbox — and it is
    /// the one place where Graph's own wording is the fastest way to find out
    /// what is wrong with a webhook.
    /// </para>
    /// </summary>
    private async Task<(GraphDiagnosis.Finding Finding, JsonElement Answer)> SendAsync(
        HttpMethod method, string url, string? body, CancellationToken token)
    {
        var access = await graph.TokenAsync(token);
        if (access is null) return (GraphDiagnosis.NoToken, default);
        var consented = GraphToken.Grants(access, GraphAuth.MailRead);

        var client = await graph.ClientAsync(token);
        if (client is null) return (GraphDiagnosis.NoToken, default);

        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var patience = CancellationTokenSource.CreateLinkedTokenSource(token);
        patience.CancelAfter(Patience);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, patience.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return (GraphDiagnosis.ForStatus(504, consented), default);
        }
        catch (HttpRequestException problem)
        {
            log.LogError(problem, "Could not reach Microsoft Graph to manage a subscription");
            return (GraphDiagnosis.ForStatus(503, consented), default);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NoContent) return (GraphDiagnosis.Connected, default);
                try
                {
                    using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(patience.Token));
                    return (GraphDiagnosis.Connected, payload.RootElement.Clone());
                }
                catch (JsonException) { return (GraphDiagnosis.Connected, default); }
            }

            var reason = await response.Content.ReadAsStringAsync(patience.Token);
            log.LogWarning("Graph refused a subscription request: {Status} {Reason}", status, Trim(reason));

            // By far the commonest 400 here, and the one nobody can diagnose
            // from the status alone: Graph called the notification URL during
            // the create and did not get the validation token back in time.
            if (status == 400)
                return (new(GraphDiagnosis.Code.Unexpected,
                    "Graph ปฏิเสธการสมัครรับแจ้งเตือน — ส่วนใหญ่คือ webhook ไม่ได้ตอบ validationToken ภายใน 10 วินาที "
                    + $"ตรวจว่า {NotifyPath} เปิดให้เรียกโดยไม่ต้องล็อกอิน (Easy Auth)", false), default);

            return (GraphDiagnosis.ForStatus(status, consented), default);
        }
    }

    /// <summary>Enough of Graph's complaint to act on, and no more than a log should carry.</summary>
    private static string Trim(string reason) =>
        reason.Length <= 400 ? reason : reason[..400];

    private static string Text(JsonElement holder, string name) =>
        holder.ValueKind == JsonValueKind.Object
        && holder.TryGetProperty(name, out var found) && found.ValueKind == JsonValueKind.String
            ? found.GetString() ?? "" : "";

    private static DateTimeOffset? Moment(JsonElement holder, string name) =>
        holder.ValueKind == JsonValueKind.Object
        && holder.TryGetProperty(name, out var found) && found.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(found.GetString(), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var moment)
                ? moment : null;
}
