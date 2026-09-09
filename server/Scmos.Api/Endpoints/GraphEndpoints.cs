using System.Text.Json;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// The mailbox connection test — one call that reads one message and says what
/// came back.
///
/// <para>
/// Step 4 of the Outlook plan, and the plan is honest that this is where the
/// work gets stuck. Nothing here is part of the mail pipeline: no message is
/// stored, no job is linked, no subscription is created. It answers one
/// question — <i>can this deployment read that mailbox, and if not, which of
/// the several reasons is it</i> — before any of the pipeline exists to fail
/// mysteriously.
/// </para>
///
/// <para>
/// <b>What comes back is deliberately thin.</b> The subject, the sender and the
/// time, because an administrator connecting a mailbox needs to see they have
/// reached the right one and a count alone does not show that. Not the body,
/// not the recipients, not the attachment names. This is a diagnostic, and a
/// diagnostic that returns whatever it happened to fetch is a mail reader that
/// nobody reviewed.
/// </para>
///
/// <para>
/// Not audited, logged. Nothing is changed by either call, and the plan already
/// routes this class of information to App Insights rather than to the audit
/// trail — which is for changes, and stays meaningful by not filling up with
/// reads.
/// </para>
/// </summary>
public static class GraphEndpoints
{
    /// <summary>What the test is asked to try.</summary>
    /// <param name="Mailbox">The address, which must be on the approved list.</param>
    public record TestBody(string? Mailbox);

    /// <summary>
    /// Graph can be slow, and this sits behind a button somebody is watching.
    /// Shorter than the shared client's default so a hung call reports rather
    /// than holds the screen for a minute and a half.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    public static void MapGraph(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/integrations/graph").WithTags("Graph");

        /*
         * Configuration readiness, without touching a mailbox. Says whether the
         * approved list is set, whether Entra will issue a token, and whether
         * that token carries Mail.Read — the two faults that can be told apart
         * before a mailbox is involved at all.
         *
         * Gated, unlike the LINE equivalent: that one reveals whether a secret
         * is set, this one names the mailboxes.
         */
        group.MapGet("/status", async (HttpContext context, IUserAccessor users,
            GraphAuth graph, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.AdministerMailbox))
                return ApiResults.Error("ดูสถานะการเชื่อมต่อ Outlook ได้เฉพาะผู้ดูแลระบบ",
                    StatusCodes.Status403Forbidden);
            if (ApiResults.NeedsSecondFactor(users, user, Capability.AdministerMailbox) is { } refusal)
                return refusal;

            var ready = await graph.ReadyAsync(token);
            return Results.Json(new
            {
                ok = ready.Ok,
                message = ready.Message,
                mailboxes = ready.Mailboxes,
                token = ready.Token,
                mailRead = ready.MailRead,
            });
        });

        /*
         * The test itself. Reads the newest message in one approved mailbox.
         */
        group.MapPost("/test", async (TestBody body, HttpContext context, IUserAccessor users,
            GraphAuth graph, ILoggerFactory logs, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.AdministerMailbox))
                return ApiResults.Error("ทดสอบการเชื่อมต่อตู้จดหมายได้เฉพาะผู้ดูแลระบบ",
                    StatusCodes.Status403Forbidden);
            if (ApiResults.NeedsSecondFactor(users, user, Capability.AdministerMailbox) is { } refusal)
                return refusal;

            var mailbox = GraphMailboxes.Normalise(body?.Mailbox);
            if (mailbox.Length == 0)
                return ApiResults.Error("ต้องระบุอีเมลของตู้จดหมาย", StatusCodes.Status400BadRequest);

            // The approved list first, before a token is even asked for. A
            // mailbox this deployment was never allowed to read should not
            // produce a Graph call at all, whatever Exchange would have said.
            if (!graph.Approves(mailbox))
                return Answer(mailbox, GraphDiagnosis.NotApproved);

            var log = logs.CreateLogger("Graph.Test");

            // Consent is read off our own token, so a 403 below means Exchange
            // scoping rather than consent. Without this the two are one answer.
            var access = await graph.TokenAsync(token);
            if (access is null) return Answer(mailbox, GraphDiagnosis.NoToken);
            var consented = GraphToken.Grants(access, GraphAuth.MailRead);

            var client = await graph.ClientAsync(token);
            if (client is null) return Answer(mailbox, GraphDiagnosis.NoToken);

            using var patience = CancellationTokenSource.CreateLinkedTokenSource(token);
            patience.CancelAfter(Patience);

            var url = $"{GraphAuth.Endpoint}/users/{Uri.EscapeDataString(mailbox)}/messages"
                + "?$top=1&$select=subject,from,receivedDateTime,hasAttachments"
                + "&$orderby=receivedDateTime%20desc";

            HttpResponseMessage response;
            try
            {
                response = await client.GetAsync(url, patience.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                log.LogWarning("Mailbox test timed out after {Seconds}s", Patience.TotalSeconds);
                return Answer(mailbox, GraphDiagnosis.ForStatus(504, consented));
            }
            catch (HttpRequestException problem)
            {
                log.LogError(problem, "Mailbox test could not reach Microsoft Graph");
                return Answer(mailbox, GraphDiagnosis.ForStatus(503, consented));
            }

            using (response)
            {
                var finding = GraphDiagnosis.ForStatus((int)response.StatusCode, consented);
                if (!finding.Ok)
                {
                    // The status and the mailbox, never the body: a Graph error
                    // body on a mail route can carry the address of whoever the
                    // message was about.
                    log.LogWarning("Mailbox test for {Mailbox} by {Who}: {Code} ({Status})",
                        mailbox, user.Signature, finding.Code, (int)response.StatusCode);
                    return Answer(mailbox, finding);
                }

                var newest = await NewestAsync(response, patience.Token);
                log.LogInformation("Mailbox test for {Mailbox} by {Who}: connected, {Found} message(s)",
                    mailbox, user.Signature, newest is null ? 0 : 1);

                return newest is null
                    ? Answer(mailbox, GraphDiagnosis.Empty)
                    : Answer(mailbox, GraphDiagnosis.Connected, newest);
            }
        });
    }

    /// <summary>One shape for every outcome, so the screen has one thing to read.</summary>
    private static IResult Answer(string mailbox, GraphDiagnosis.Finding finding, object? newest = null) =>
        Results.Json(new { ok = finding.Ok, code = finding.Code, message = finding.Message, mailbox, newest });

    /// <summary>
    /// The one message, reduced to what proves the mailbox is the right one.
    ///
    /// Returns null for an empty mailbox and for anything unreadable — an
    /// answer that cannot be parsed is reported as "connected, nothing to
    /// show" rather than as a failure, because the connection genuinely
    /// worked and the parsing is ours to fix.
    /// </summary>
    private static async Task<object?> NewestAsync(HttpResponseMessage response, CancellationToken token)
    {
        try
        {
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token));
            if (!payload.RootElement.TryGetProperty("value", out var value)
                || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0) return null;

            var message = value[0];
            var from = message.TryGetProperty("from", out var sender)
                && sender.TryGetProperty("emailAddress", out var address)
                    ? Text(address, "address")
                    : "";

            return new
            {
                subject = Text(message, "subject"),
                from,
                receivedAt = Text(message, "receivedDateTime"),
                hasAttachments = message.TryGetProperty("hasAttachments", out var has)
                    && has.ValueKind == JsonValueKind.True,
            };
        }
        catch (JsonException) { return null; }
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var found) && found.ValueKind == JsonValueKind.String
            ? found.GetString() ?? "" : "";
}
