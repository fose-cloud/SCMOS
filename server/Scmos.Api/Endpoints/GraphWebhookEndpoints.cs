using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// Where Graph delivers. Take it, prove it, write it down, answer.
///
/// <para>
/// It does no business work at all, for the reason the LINE webhook does none:
/// Graph redelivers anything it did not get a prompt answer to, so an endpoint
/// that fetched a message, extracted its identifiers and matched a job before
/// replying would be redelivered mid-way and do the work twice. This writes the
/// smallest durable thing — that a message with this id arrived in this mailbox
/// — and returns. The worker picks it up.
/// </para>
///
/// <para>
/// <b>Not behind the usual sign-in.</b> Easy Auth sits in front of this API and
/// Microsoft cannot sign in to it, so these two paths have to be excluded or
/// nothing will ever arrive — see the plan's section 4. The <c>clientState</c>
/// on each item is what authenticates a delivery, which is why it is compared
/// the way it is.
/// </para>
///
/// <para>
/// <b>The validation handshake comes first, always.</b> Graph calls this URL
/// while a subscription is being created and expects its token echoed back as
/// plain text within ten seconds. It happens before the body is read, before
/// the database is touched, and before anything can fail — a create that times
/// out here is a subscription that is never made, and every later step depends
/// on it.
/// </para>
/// </summary>
public static class GraphWebhookEndpoints
{
    public static void MapGraphWebhook(this IEndpointRouteBuilder routes)
    {
        /*
         * Messages arriving.
         *
         * Mapped at the constant itself rather than at a group prefix plus a
         * suffix, so the route this API serves and the URL Graph is handed are
         * the same string. Two halves that have to agree are two halves that
         * can stop agreeing, and the symptom would be every notification
         * answered with a 404 that nothing here would ever see.
         */
        routes.MapPost(GraphSubscriptionService.NotifyPath,
            async (HttpContext context, ScmosDbContext db, ILoggerFactory logs, CancellationToken token) =>
        {
            var log = logs.CreateLogger("Graph.Notify");

            if (Handshake(context) is { } answer) return answer;

            var body = await ReadAsync(context, token);
            if (body is null) return Results.Accepted();

            using (body)
            {
                var notices = GraphNotifications.ReadNotices(body.RootElement);
                if (notices.Count == 0) return Results.Accepted();

                var (stored, refused, duplicates) = await StoreAsync(db, notices, log, token);
                if (refused > 0)
                    // Counted, never explained. A run of these is somebody
                    // probing the endpoint, and an unauthenticated caller told
                    // why they failed is being told how to get closer.
                    log.LogWarning("Graph webhook refused {Refused} notification(s) on clientState", refused);

                log.LogInformation("Graph webhook stored {Stored}, already had {Duplicates}",
                    stored, duplicates);
            }

            // Accepted, not OK. Graph asked us to take delivery; the work
            // happens later, and saying so is the honest status.
            return Results.Accepted();
        }).WithTags("Graph");

        /*
         * The subscription itself being in trouble. Not optional: this is how
         * Graph says it has stopped, and the alternative to answering it is a
         * mailbox that goes quiet with nothing to show for it.
         */
        routes.MapPost(GraphSubscriptionService.LifecyclePath,
            async (HttpContext context, ScmosDbContext db, ILoggerFactory logs, CancellationToken token) =>
        {
            var log = logs.CreateLogger("Graph.Lifecycle");

            if (Handshake(context) is { } answer) return answer;

            var body = await ReadAsync(context, token);
            if (body is null) return Results.Accepted();

            using (body)
            {
                foreach (var one in GraphNotifications.ReadLifecycle(body.RootElement))
                {
                    var row = await db.GraphSubscriptions
                        .FirstOrDefaultAsync(entry => entry.SubscriptionId == one.SubscriptionId, token);

                    if (row is null || !GraphSubscriptions.StateMatches(row.ClientState, one.ClientState))
                    {
                        log.LogWarning("Graph lifecycle event refused on clientState");
                        continue;
                    }

                    switch (one.Event)
                    {
                        // Both mean the same thing from this side: it is no
                        // longer usable, and the hourly loop should make
                        // another. EXPIRED rather than DELETED, because DELETED
                        // reads as something we chose.
                        case GraphNotifications.Event.Reauthorize:
                        case GraphNotifications.Event.Removed:
                            row.Status = MailSubscription.Expired;
                            await db.SaveChangesAsync(token);
                            log.LogWarning("Graph says subscription {Id} needs making again ({Event})",
                                row.SubscriptionId, one.Event);
                            break;

                        // Graph gave up on delivering something. Nothing here
                        // can say what, so nothing here can fetch it: recovery
                        // is the catch-up read from the mailbox's high-water
                        // mark, which is the worker's. Logged loudly because it
                        // is the one fault this integration cannot otherwise
                        // detect — mail that silently never arrived.
                        case GraphNotifications.Event.Missed:
                            log.LogWarning(
                                "Graph MISSED notifications for subscription {Id}; the next catch-up read "
                                + "from the mailbox's last synced time is what recovers them",
                                row.SubscriptionId);
                            break;

                        default:
                            log.LogInformation("Graph sent lifecycle event {Event}, which nothing acts on",
                                one.Event);
                            break;
                    }
                }
            }

            return Results.Accepted();
        }).WithTags("Graph");
    }

    /// <summary>
    /// The validation handshake, or null when this is not one.
    ///
    /// <para>
    /// Echoed as <c>text/plain</c>, which is what Graph reads and what stops the
    /// echo being anything else. The token is checked for being a token first —
    /// this repeats an unauthenticated caller's own input back at them, and the
    /// smallest version of that is the safest one.
    /// </para>
    /// </summary>
    private static IResult? Handshake(HttpContext context)
    {
        if (!context.Request.Query.TryGetValue("validationToken", out var sent)) return null;

        var token = sent.ToString();
        // Refused rather than echoed. Graph's token is short and opaque; a long
        // or odd one is not the handshake, and answering it would make this an
        // endpoint that repeats whatever it is given.
        if (!GraphNotifications.IsUsableToken(token)) return Results.BadRequest();

        return Results.Text(token, "text/plain", Encoding.UTF8);
    }

    /// <summary>The body, or null when there is not a readable one.</summary>
    private static async Task<JsonDocument?> ReadAsync(HttpContext context, CancellationToken token)
    {
        try
        {
            return await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: token);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Write down that these messages arrived, once each.
    ///
    /// <para>
    /// A stub row in <c>emails</c>: the mailbox, the Graph id, and the moment it
    /// reached us. Not a new queue table, because the one the schema already has
    /// is exactly this — the unique key on <c>(MailboxId, GraphMessageId)</c> is
    /// the whole of idempotency the specification asked for, and
    /// <c>emails_queue_idx</c> is described in the model as "what the worker
    /// asks for: the oldest thing not yet dealt with". The body, the sender and
    /// the participants are the worker's to fill in; a notification does not
    /// carry them.
    /// </para>
    /// </summary>
    private static async Task<(int Stored, int Refused, int Duplicates)> StoreAsync(ScmosDbContext db,
        IReadOnlyList<GraphNotifications.Notice> notices, ILogger log, CancellationToken token)
    {
        var arrived = DateTimeOffset.UtcNow;
        int stored = 0, refused = 0, duplicates = 0;

        // One lookup for the whole batch. A delivery is usually several
        // notifications from one subscription.
        var wanted = notices.Select(one => one.SubscriptionId).Distinct().ToList();
        var subscriptions = await db.GraphSubscriptions
            .Where(one => wanted.Contains(one.SubscriptionId))
            .ToDictionaryAsync(one => one.SubscriptionId, token);

        foreach (var notice in notices)
        {
            // An unknown subscription and a wrong secret are the same answer.
            // Which of the two it was is not something a caller is told.
            if (!subscriptions.TryGetValue(notice.SubscriptionId, out var subscription)
                || !GraphSubscriptions.StateMatches(subscription.ClientState, notice.ClientState))
            {
                refused++;
                continue;
            }

            // Checked here only to keep the ordinary case off the exception
            // path. Graph redelivers whatever it did not get a fast enough
            // answer to, so a repeat is expected rather than exceptional — the
            // unique index below is the actual guard.
            if (await db.Emails.AsNoTracking().AnyAsync(one =>
                    one.MailboxId == subscription.MailboxId
                    && one.GraphMessageId == notice.MessageId, token))
            {
                duplicates++;
                continue;
            }

            db.Emails.Add(new Email
            {
                MailboxId = subscription.MailboxId,
                GraphMessageId = notice.MessageId,
                // When it reached us, not when it was sent — a notification does
                // not say. The worker replaces this with the real time once it
                // has the message, and until then this is what orders the queue.
                ReceivedAt = arrived,
                CreatedAt = arrived,
                ProcessingStatus = MailProcessing.Received,
            });
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
                // Two deliveries of the same notification in the same moment.
                // The index settled it, and losing that race is the correct
                // outcome rather than something to answer with an error.
                db.ChangeTracker.Clear();
                log.LogInformation("Graph webhook lost a race on a message already stored");
                return (0, refused, duplicates + stored);
            }
        }

        return (stored, refused, duplicates);
    }
}
