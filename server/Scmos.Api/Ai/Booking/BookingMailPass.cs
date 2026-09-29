using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Ai.Booking;

/// <summary>
/// The Booking Agent's pass over mail the matcher left unplaced (AI Agent
/// Platform specification §27, source "Outlook"): each message received within
/// <c>AI__BookingMailHours</c> that no job holds — no confirmed or suggested
/// link — is read once. The model says whether it asks for transport and
/// proposes the fields; <see cref="BookingVerification"/> keeps what the words
/// say. A booking becomes an open draft in the decision log for a person to
/// open as a pre-filled add-job form; anything else is recorded as read and
/// closed, so it is never read (or paid for) twice. Nothing is created,
/// linked, sent or replied to.
///
/// <para>
/// A draft whose message a person later links to a job is resolved with that
/// job. At most <c>AI__BookingMailPerPass</c> messages a pass, oldest first;
/// a provider failure ends the pass and the message waits for the next one.
/// The run is audited under the one non-person identity the audit allows
/// (<see cref="AiAuditRules.SystemUser"/>), with the message's id as its only
/// evidence key — never its text.
/// </para>
///
/// <para>
/// How a draft is stored, so the screen can rebuild the form from it: each
/// accepted field is a fact whose text is the value and whose source is
/// <c>email:{id}.text#{field}</c>; the words it came from are an observation
/// sourced <c>quote#{field}</c>; the category is the rule reference
/// <c>Category:{IMPORT|EXPORT|DELIVERY}</c>.
/// </para>
/// </summary>
public sealed class BookingMailPass(ScmosDbContext db, IBookingTextReader reader, IAiExecutionAudit audit, AiRunLimiter limiter,
    AiDecisionLog decisions, IOptions<AiOptions> options, TimeProvider clock, ILogger<BookingMailPass> log,
    IOptions<OpenAiOptions>? providerOptions = null)
{
    public const string NotBooking = "NOT_BOOKING";
    public const string FieldSource = ".text#";
    public const string QuotePrefix = "quote#";
    public const string CategoryPrefix = "Category:";

    public async Task<ScanSummary> PassAsync(AgentDefinition agent, bool shadow, AiAutonomy autonomy,
        IReadOnlyCollection<string> knownCustomers, CancellationToken token)
    {
        var ai = options.Value;
        if (!reader.Configured) return new(agent.Id, "not_configured", 0, 0, 0, 0, 0, 0, 0);
        var now = clock.GetUtcNow();

        // Drafts whose message now belongs to a job: a person made the job and linked the mail.
        var resolved = 0;
        var open = await db.AiDecisions.Where(one => one.AgentId == agent.Id && one.EntityType == "email" && one.Status == AiDecisionLog.Open)
            .ToListAsync(token);
        if (open.Count > 0)
        {
            var ids = open.Select(one => long.TryParse(one.EntityId, out var id) ? id : 0).Where(id => id > 0).ToList();
            var linked = await db.EmailJobLinks.AsNoTracking().Where(link => ids.Contains(link.EmailId) && link.Status == MailLink.Confirmed)
                .Select(link => new { link.EmailId, link.JobKey, link.ConfirmedBy, link.ConfirmedAt }).ToListAsync(token);
            foreach (var row in open)
                if (linked.FirstOrDefault(link => link.EmailId.ToString() == row.EntityId) is { } link)
                {
                    row.Status = AiDecisionLog.Resolved;
                    row.DecidedAt = now;
                    row.DecidedBy = "system";
                    row.HumanChoice = link.JobKey.Length <= 400 ? link.JobKey : link.JobKey[..400];
                    row.OverrideReason = "อีเมลนี้ถูกจับคู่กับงานแล้ว" + (link.ConfirmedBy.Length > 0 ? $" ({link.ConfirmedBy})" : "");
                    resolved++;
                }
            if (resolved > 0) await db.SaveChangesAsync(token);
        }

        // What is left to read: recent, placed by nothing, never read before.
        var since = now.AddHours(-ai.BookingMailHours);
        var candidates = await db.Emails.AsNoTracking()
            .Where(mail => mail.ReceivedAt >= since
                && (mail.ProcessingStatus == MailProcessing.Processed || mail.ProcessingStatus == MailProcessing.NeedReview)
                && !db.EmailJobLinks.Any(link => link.EmailId == mail.Id && (link.Status == MailLink.Confirmed || link.Status == MailLink.Suggested)))
            .OrderBy(mail => mail.ReceivedAt).ThenBy(mail => mail.Id)
            .Select(mail => new { mail.Id, mail.Subject, mail.FromAddress, mail.FromName, mail.BodyText, mail.ReceivedAt })
            .Take(200).ToListAsync(token);
        var candidateIds = candidates.Select(mail => mail.Id.ToString()).ToList();
        var read = (await db.AiDecisions.AsNoTracking()
                .Where(one => one.AgentId == agent.Id && one.EntityType == "email" && candidateIds.Contains(one.EntityId))
                .Select(one => one.EntityId).ToListAsync(token)).ToHashSet(StringComparer.Ordinal);
        var batch = candidates.Where(mail => !read.Contains(mail.Id.ToString())).Take(ai.BookingMailPerPass).ToList();

        int created = 0, closed = 0, refused = 0;
        var code = "ok";
        foreach (var mail in batch)
        {
            var text = $"{mail.Subject}\n\n{mail.BodyText}";
            using var lease = limiter.TryEnter();
            if (lease is null) { code = "busy"; break; }
            var (runId, reading, failure) = await ReadAsync(mail.Id, text, token);
            if (reading is null) { code = failure; break; }
            var received = DateOnly.FromDateTime(mail.ReceivedAt.ToOffset(Formats.Zone).DateTime);
            var result = reading.IsBooking && reading.Category != "NONE"
                ? Draft(mail.Id, mail.Subject, mail.FromName, mail.FromAddress, mail.ReceivedAt, reading,
                    BookingVerification.Check(reading.Category, text, reading.Fields, knownCustomers, received))
                : NotABooking(mail.Id, mail.Subject, mail.FromAddress, mail.ReceivedAt);
            var (row, problems) = decisions.Stage(result, runId, "", shadow, autonomy);
            if (row is null)
            {
                refused++;
                log.LogWarning("Booking draft for mail {Id} refused by the decision contract: {Problems}", mail.Id, string.Join("; ", problems));
                continue;
            }
            row.CreatedAt = now;
            if (result.DecisionType == NotBooking.ToLowerInvariant())
            {
                row.Status = AiDecisionLog.Resolved;
                row.DecidedAt = now;
                row.DecidedBy = "system";
                row.OverrideReason = "ไม่ใช่คำขอจองรถ";
                closed++;
            }
            else created++;
            await db.SaveChangesAsync(token);
        }
        if (created + closed + resolved > 0)
            log.LogInformation("booking-agent: {Created} drafts, {Closed} not bookings, {Resolved} linked, over {Batch} messages",
                created, closed, resolved, batch.Count);
        return new ScanSummary(agent.Id, code, batch.Count, created, created, 0, 0, resolved + closed, refused);
    }

    /// <summary>One message read under the audit; no reading, with why, when the audit or the provider could not.</summary>
    private async Task<(string RunId, BookingReading? Reading, string Failure)> ReadAsync(long mailId, string text, CancellationToken token)
    {
        var runId = Guid.NewGuid().ToString("N");
        var toolCallId = Guid.NewGuid().ToString("N");
        var model = providerOptions?.Value.Model is { Length: > 0 } named ? named : "unconfigured";
        string[] keys = [$"mail:{mailId}"];
        var scope = new AiReadScope(true, null);
        var toolStarted = false;
        async Task Record(string kind, string status, bool withEvidence = false, CancellationToken? cleanup = null)
        {
            var withTool = kind is "tool_started" or "tool_completed" || (kind == "run_completed" && toolStarted);
            var step = kind is "tool_started" or "tool_completed" ? 1 : kind == "run_completed" ? (toolStarted ? 1 : 0) : (int?)null;
            await audit.RecordAsync(new(runId, AiAuditRules.SystemUser, AiAuditRules.SystemRole, BookingAgent.Id, kind,
                withTool ? BookingAgent.Tool : null, status, clock.GetUtcNow(),
                withEvidence ? 1 : null, withEvidence ? 1 : null, scope, null,
                withTool ? toolCallId : null, model, withTool ? "mail" : null,
                withTool ? 1 : null, withEvidence ? keys : null, "", step), cleanup ?? token);
        }
        var started = false;
        try
        {
            if (!await audit.CheckReadyAsync(token)) return (runId, null, "audit_not_ready");
            await Record("run_started", "running");
            started = true;
            await Record("tool_started", "running");
            toolStarted = true;
            BookingReadResult result;
            try { result = await reader.ReadAsync(null, text, token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { result = new(null, "timeout", StatusCodes.Status502BadGateway); }
            var status = result.Reading is not null ? "succeeded"
                : result.Status == StatusCodes.Status429TooManyRequests ? "provider_busy" : "provider_unavailable";
            await Record("tool_completed", status, result.Reading is not null);
            await Record("run_completed", status, result.Reading is not null);
            return (runId, result.Reading, status);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (started) await Cleanup("cancelled");
            throw;
        }
        catch (Exception problem)
        {
            log.LogWarning(problem, "Booking mail read for {Id} could not be audited", mailId);
            if (started) await Cleanup("audit_failed");
            return (runId, null, "audit_failed");
        }

        async Task Cleanup(string status)
        {
            using var window = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                if (toolStarted) await Record("tool_completed", status, cleanup: window.Token);
                await Record("run_completed", status, cleanup: window.Token);
            }
            catch (Exception) { /* the run stays incomplete in the audit, which is what happened */ }
        }
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..max];

    private static List<AgentFinding> About(long id, string subject, string from, DateTimeOffset received) =>
    [
        new($"เรื่อง: {Cut(Formats.Clean(subject) is { Length: > 0 } s ? s : "(ไม่มีหัวเรื่อง)", 300)}", $"email:{id}.subject"),
        new($"จาก: {Cut(Formats.Clean(from), 200)}", $"email:{id}.from"),
        new($"ได้รับ {received.ToOffset(Formats.Zone):dd/MM/yyyy HH:mm}", $"email:{id}.receivedAt"),
    ];

    public static AgentResult Draft(long id, string subject, string fromName, string fromAddress, DateTimeOffset received,
        BookingReading reading, BookingDraft draft)
    {
        var facts = About(id, subject, fromName.Length > 0 ? $"{fromName} <{fromAddress}>" : fromAddress, received);
        var observations = new List<AgentFinding>();
        foreach (var field in draft.Fields.Where(one => one.Verified))
        {
            facts.Add(new(Cut(field.Value, 400), $"email:{id}{FieldSource}{field.Field}"));
            observations.Add(new($"“{Cut(field.Quote, 390)}”", QuotePrefix + field.Field));
        }
        foreach (var field in draft.Fields.Where(one => !one.Verified && one.Proposed.Length > 0))
            observations.Add(new(Cut($"ไม่รับ {field.Field}: {field.Proposed} — {field.Reason}", 400), null));
        var inferences = new List<AgentFinding>
        {
            new(Cut($"เป็นคำขอจองรถ งาน {draft.Category}" + (reading.CategoryQuote.Length > 0 ? $" (จาก “{reading.CategoryQuote}”)" : ""), 400)),
        };
        var rules = new List<AgentFinding> { new("เก็บเฉพาะช่องที่ข้อความต้นฉบับยืนยันได้ และลูกค้าที่มีในทะเบียน", "BookingVerification") };
        if (draft.Missing.Count > 0) rules.Add(new($"ยังไม่มี: {string.Join(", ", draft.Missing)}", "BookingVerification.Essential"));
        var summary = $"{Cut(Formats.Clean(subject) is { Length: > 0 } s ? s : "(ไม่มีหัวเรื่อง)", 200)} · ร่างงาน {draft.Category}"
            + (draft.Missing.Count > 0 ? " · ข้อมูลไม่ครบ" : "");
        return new AgentResult(BookingAgent.Id, BookingAgent.DecisionType, "email", id.ToString(),
            draft.Missing.Count > 0 ? AgentResultRules.InsufficientInformation : AgentResultRules.Completed, summary,
            facts.Take(AgentResultRules.MaxItems).ToList(), rules, observations.Take(AgentResultRules.MaxItems).ToList(), inferences,
            [new("เปิดฟอร์มเพิ่มงานจากร่างนี้ ตรวจและเติมช่องที่ขาด แล้วบันทึก")], [],
            [CategoryPrefix + draft.Category, "BookingVerification"], [$"email:{id}"], "");
    }

    public static AgentResult NotABooking(long id, string subject, string from, DateTimeOffset received) =>
        new(BookingAgent.Id, NotBooking.ToLowerInvariant(), "email", id.ToString(), AgentResultRules.Completed,
            $"{Cut(Formats.Clean(subject) is { Length: > 0 } s ? s : "(ไม่มีหัวเรื่อง)", 200)} · ไม่ใช่คำขอจองรถ",
            About(id, subject, from, received), [], [], [new("ข้อความนี้ไม่ได้ขอรถขนส่ง")], [], [],
            ["Classification:not_booking"], [$"email:{id}"], "");
}
