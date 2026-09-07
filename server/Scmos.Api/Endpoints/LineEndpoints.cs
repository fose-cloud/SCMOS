using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Endpoints;

/// <summary>
/// The LINE webhook: take delivery, prove it, store it, answer.
///
/// <para>
/// It does no business work at all. LINE retries a delivery it did not get a
/// prompt answer to, and a webhook that parsed a message, looked up a job and
/// wrote a status update before replying would be retried mid-way and do the
/// work twice. So this writes the raw event and returns; a worker picks it up.
/// </para>
///
/// <para>
/// Not behind the usual sign-in. Microsoft's Easy Auth sits in front of this
/// API and LINE cannot sign in to it, so this path has to be excluded — see
/// docs/integrations/SETUP.md. The signature is what authenticates a delivery,
/// which is why <see cref="LineSignature"/> is written the way it is.
/// </para>
/// </summary>
public static class LineEndpoints
{
    /// <summary>Where the channel secret and the on/off switch live in configuration.</summary>
    public const string SecretKey = "Line:ChannelSecret";
    public const string EnabledKey = "Line:Enabled";

    public static void MapLine(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/integrations/line").WithTags("Line");

        // The operator-facing half: the review queue, the approval, and the
        // group-to-supplier mapping. Its own file, because everything below is
        // authenticated by a signature and everything there is behind sign-in.
        group.MapLineReview();

        /*
         * What the Integrations screen probes. Says whether the integration is
         * switched on and whether a secret is configured — never the secret,
         * and never any part of it.
         */
        group.MapGet("/status", (IConfiguration config) =>
        {
            var enabled = config.GetValue(EnabledKey, false);
            var configured = !string.IsNullOrWhiteSpace(config[SecretKey]);
            return Results.Json(new
            {
                enabled,
                configured,
                webhook = "/api/integrations/line/webhook",
                message = !enabled ? "ปิดใช้งานอยู่ — ตั้ง Line__Enabled เป็น true เมื่อพร้อม"
                    : configured ? "พร้อมรับข้อความจาก LINE"
                    : "เปิดใช้งานแล้วแต่ยังไม่ได้ตั้ง Line__ChannelSecret",
            });
        });

        group.MapPost("/webhook", async (HttpContext context, IConfiguration config,
            ScmosDbContext db, ILoggerFactory logs, CancellationToken token) =>
        {
            var log = logs.CreateLogger("Line.Webhook");

            // The raw bytes, before anything parses them. The signature is over
            // what arrived, and a JSON round trip changes whitespace and key
            // order — see LineSignature.
            context.Request.EnableBuffering();
            string body;
            using (var reader = new StreamReader(context.Request.Body, leaveOpen: true))
            {
                body = await reader.ReadToEndAsync(token);
            }
            context.Request.Body.Position = 0;

            var header = context.Request.Headers["x-line-signature"].ToString();
            if (!LineSignature.Verify(config[SecretKey], header, body))
            {
                // Counted, because a run of these is somebody probing the
                // endpoint. Never says which part failed, and never logs the
                // body — an unauthenticated caller told why it failed is being
                // told how to get closer.
                log.LogWarning("LINE webhook rejected: signature did not verify");
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            // Off means stored and not acted on, rather than refused. A webhook
            // that 403s while the integration is being switched off would make
            // LINE retry and eventually disable the endpoint at their end.
            if (!config.GetValue(EnabledKey, false))
            {
                log.LogInformation("LINE webhook accepted while disabled; not stored");
                return Results.Ok();
            }

            var received = DateTimeOffset.UtcNow;
            var stored = 0;
            var duplicates = 0;

            foreach (var one in Events(body, log))
            {
                // The unique index on line_message_id is the real guard, and it
                // is checked here as well only to keep the common retry off the
                // exception path. LINE redelivers whatever it did not get a
                // fast enough answer to, so this is the ordinary case.
                if (await db.LineEvents.AsNoTracking()
                    .AnyAsync(row => row.LineMessageId == one.LineMessageId, token))
                {
                    duplicates++;
                    continue;
                }

                one.ReceivedAt = received;
                db.LineEvents.Add(one);
                stored++;
            }

            if (stored > 0)
            {
                try
                {
                    await db.SaveChangesAsync(token);
                }
                catch (DbUpdateException)
                {
                    // Two deliveries of the same message in the same second.
                    // The index settled it; losing is the correct outcome and
                    // is not an error worth answering with.
                    db.ChangeTracker.Clear();
                    log.LogInformation("LINE webhook lost a race on a duplicate message");
                    return Results.Ok();
                }
            }

            log.LogInformation("LINE webhook stored {Stored}, skipped {Duplicates} already seen",
                stored, duplicates);

            // 200 whatever happened above, as long as the delivery was genuine.
            // Anything else makes LINE retry a message already stored.
            return Results.Ok();
        });
    }

    /// <summary>
    /// The text messages in a webhook body, as rows ready to store.
    ///
    /// <para>
    /// Everything that is not a text message is dropped here rather than stored
    /// and ignored: stickers, images, joins and leaves arrive in the same
    /// deliveries and would fill the table with rows no worker will ever read.
    /// </para>
    ///
    /// <para>
    /// A body that will not parse is logged and yields nothing. It cannot be a
    /// genuine LINE delivery — it passed the signature, so it is what LINE
    /// sent — which makes it a shape this code has not met, and the log is how
    /// anybody finds out.
    /// </para>
    /// </summary>
    private static List<LineEvent> Events(string body, ILogger log)
    {
        var rows = new List<LineEvent>();
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(body).RootElement;
        }
        catch (JsonException error)
        {
            log.LogError("LINE webhook body did not parse: {Message}", error.Message);
            return rows;
        }

        if (!root.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
            return rows;

        foreach (var one in events.EnumerateArray())
        {
            var type = Text(one, "type");
            if (type != "message") continue;
            if (!one.TryGetProperty("message", out var message)) continue;
            if (Text(message, "type") != "text") continue;

            var messageId = Text(message, "id");
            if (messageId.Length == 0) continue;

            var source = one.TryGetProperty("source", out var src) ? src : default;
            rows.Add(new LineEvent
            {
                WebhookEventId = Text(one, "webhookEventId"),
                LineMessageId = messageId,
                LineGroupId = source.ValueKind == JsonValueKind.Object ? Text(source, "groupId") : "",
                LineUserId = source.ValueKind == JsonValueKind.Object ? Text(source, "userId") : "",
                MessageType = "text",
                RawText = Text(message, "text"),
                // The whole event, not the whole body: one row is one message,
                // and a body carrying five would otherwise store the other four
                // five times over.
                RawPayload = one.GetRawText(),
                ProcessingStatus = LineProcessing.Received,
            });
        }

        return rows;
    }

    private static string Text(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object
        && parent.TryGetProperty(name, out var found)
        && found.ValueKind == JsonValueKind.String
            ? found.GetString() ?? ""
            : "";
}
