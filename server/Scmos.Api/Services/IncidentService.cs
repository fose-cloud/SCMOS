using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Auth;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record IncidentView(
    long Id, string Reference, string JobKey, string Kind, string Category, string Title, string Stage,
    string What, string Where, string When, string Who, string Why, string How,
    string AiSummary, string RootCause, string CorrectiveAction, string PreventiveAction,
    string ResponsiblePerson, string DueDate, string FollowUpNote, string EffectivenessNote,
    string ApprovedBy, DateTimeOffset? ApprovedAt,
    string RaisedBy, DateTimeOffset RaisedAt, bool Overdue,
    IReadOnlyList<DocumentView> Evidence,

    /* The rest of ISO-FRM-TH-ISO-08-09. Appended rather than slotted in beside
     * the fields they belong with: this is a positional record, and moving one
     * argument shifts every one after it into the wrong field — a mistake that
     * compiles, because they are all strings. */
    string Company, string Grade, string Source, string NcClause,
    string Team, string RequestedBy, string RequestedOn,
    string ImmediateAction, string ImmediateBy, string ImmediateDue,
    string DocumentsToRevise, string FollowUpBy, string ReviewedBy,
    string ApprovalOutcome, string ApprovalNote, string TeamNote);

public record IncidentResult(bool Ok, string Message, long? Id = null);

/// <summary>
/// CAR / PAR.
///
/// The stages follow the quality process the team already runs on paper, so a
/// case moved into the system is the same case rather than a new shape somebody
/// has to learn. The one rule the system enforces is the last one: a case
/// cannot close without a person's name against it. The AI writes the summary;
/// it does not sign off the action.
/// </summary>
public class IncidentService(ScmosDbContext db)
{
    public static readonly string[] Stages =
        ["open", "analysis", "action", "follow-up", "monitoring", "approval", "closed"];

    public static readonly string[] Categories =
        ["accident", "damage", "delay", "safety", "quality", "other"];

    public async Task<IReadOnlyList<IncidentView>> ListAsync(string? stage, string? kind, CancellationToken token)
    {
        var query = db.IncidentCases.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(stage) && stage != "All") query = query.Where(c => c.Stage == stage);
        if (!string.IsNullOrWhiteSpace(kind) && kind != "All") query = query.Where(c => c.Kind == kind);

        var cases = await query.OrderByDescending(c => c.Id).Take(300).ToListAsync(token);
        var ids = cases.Select(c => (long?)c.Id).ToHashSet();
        var evidence = await db.Documents.AsNoTracking()
            .Where(e => ids.Contains(e.CaseId)).ToListAsync(token);

        return cases.Select(c => Describe(c,
            evidence.Where(e => e.CaseId == c.Id).Select(DocumentService.Describe).ToList())).ToList();
    }

    public async Task<IncidentView?> ReadAsync(long id, CancellationToken token)
    {
        var record = await db.IncidentCases.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, token);
        if (record is null) return null;
        var evidence = await db.Documents.AsNoTracking().Where(e => e.CaseId == id).ToListAsync(token);
        return Describe(record, evidence.Select(DocumentService.Describe).ToList());
    }

    /// <summary>
    /// Opens a case. The reference is issued here rather than typed, so two
    /// people raising a case at once cannot pick the same number.
    /// </summary>
    public async Task<IncidentResult> RaiseAsync(string jobKey, string kind, string category,
        string title, string by, CancellationToken token)
    {
        if (title.Trim().Length == 0) return new IncidentResult(false, "ต้องระบุหัวข้อ");

        var wantedKind = kind.Trim().ToUpperInvariant() == "PAR" ? "PAR" : "CAR";
        var wantedCategory = Categories.Contains(category.Trim().ToLowerInvariant())
            ? category.Trim().ToLowerInvariant() : "other";

        var year = DateTimeOffset.Now.ToString("yy");
        var prefix = $"{wantedKind}-{year}-";
        var taken = await db.IncidentCases.AsNoTracking()
            .Where(c => c.Reference.StartsWith(prefix))
            .Select(c => c.Reference).ToListAsync(token);
        var next = taken.Count + 1;
        while (taken.Contains($"{prefix}{next:D3}")) next++;

        var record = new IncidentCase
        {
            Reference = $"{prefix}{next:D3}",
            JobKey = jobKey.Trim(),
            Kind = wantedKind,
            Category = wantedCategory,
            Title = title.Trim(),
            Stage = "open",
            RaisedBy = by,
            RaisedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.IncidentCases.Add(record);
        await db.SaveChangesAsync(token);

        return new IncidentResult(true, $"เปิดเคส {record.Reference} แล้ว", record.Id);
    }

    /// <summary>Fills in the analysis. Every field is optional; blanks leave what is there.</summary>
    public async Task<IncidentResult> UpdateAsync(long id, Dictionary<string, string> fields,
        string by, CancellationToken token)
    {
        var record = await db.IncidentCases.FirstOrDefaultAsync(c => c.Id == id, token);
        if (record is null) return new IncidentResult(false, "ไม่พบเคสนี้");
        if (record.Stage == "closed") return new IncidentResult(false, "เคสนี้ปิดแล้ว — เปิดใหม่ก่อนจึงจะแก้ได้");

        string Take(string key) => fields.TryGetValue(key, out var value) ? value.Trim() : "";
        void Set(string key, Action<string> apply)
        {
            var value = Take(key);
            if (value.Length > 0) apply(value);
        }

        Set("what", v => record.What = v);
        Set("where", v => record.Where = v);
        Set("when", v => record.When = v);
        Set("who", v => record.Who = v);
        Set("why", v => record.Why = v);
        Set("how", v => record.How = v);
        Set("aiSummary", v => record.AiSummary = v);
        Set("rootCause", v => record.RootCause = v);
        Set("correctiveAction", v => record.CorrectiveAction = v);
        Set("preventiveAction", v => record.PreventiveAction = v);
        Set("responsiblePerson", v => record.ResponsiblePerson = v);
        Set("dueDate", v => record.DueDate = v);
        Set("followUpNote", v => record.FollowUpNote = v);
        Set("effectivenessNote", v => record.EffectivenessNote = v);
        Set("category", v => { if (Categories.Contains(v.ToLowerInvariant())) record.Category = v.ToLowerInvariant(); });

        // The rest of ISO-FRM-TH-ISO-08-09. Same shape as above: a field only
        // changes when something is sent for it, so a screen that knows about
        // half the form cannot blank the other half by saving.
        Set("company", v => record.Company = v);
        Set("grade", v => record.Grade = v);
        Set("source", v => record.Source = v);
        Set("ncClause", v => record.NcClause = v);
        Set("team", v => record.Team = v);
        Set("requestedBy", v => record.RequestedBy = v);
        Set("requestedOn", v => record.RequestedOn = v);
        Set("immediateAction", v => record.ImmediateAction = v);
        Set("immediateBy", v => record.ImmediateBy = v);
        Set("immediateDue", v => record.ImmediateDue = v);
        Set("documentsToRevise", v => record.DocumentsToRevise = v);
        Set("followUpBy", v => record.FollowUpBy = v);
        Set("reviewedBy", v => record.ReviewedBy = v);
        Set("approvalOutcome", v => record.ApprovalOutcome = v);
        Set("approvalNote", v => record.ApprovalNote = v);
        Set("teamNote", v => record.TeamNote = v);

        record.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);
        return new IncidentResult(true, "บันทึกแล้ว", id);
    }

    /// <summary>
    /// Moves the case along, refusing to skip the things that make a CAR/PAR
    /// worth having. A corrective action without a root cause is a guess, and an
    /// action without an owner and a date is a wish.
    /// </summary>
    public async Task<IncidentResult> AdvanceAsync(long id, string by, string role, CancellationToken token)
    {
        var record = await db.IncidentCases.FirstOrDefaultAsync(c => c.Id == id, token);
        if (record is null) return new IncidentResult(false, "ไม่พบเคสนี้");

        var position = Array.IndexOf(Stages, record.Stage);
        if (position < 0 || position >= Stages.Length - 1)
            return new IncidentResult(false, "เคสนี้ปิดแล้ว");

        var next = Stages[position + 1];

        var missing = next switch
        {
            "action" when record.RootCause.Trim().Length == 0 => "ต้องระบุสาเหตุที่แท้จริงก่อน",
            "follow-up" when record.CorrectiveAction.Trim().Length == 0 => "ต้องระบุการแก้ไขก่อน",
            "follow-up" when record.ResponsiblePerson.Trim().Length == 0 => "ต้องระบุผู้รับผิดชอบก่อน",
            "follow-up" when record.DueDate.Trim().Length == 0 => "ต้องระบุกำหนดเสร็จก่อน",
            "monitoring" when record.PreventiveAction.Trim().Length == 0 => "ต้องระบุการป้องกันก่อน",
            "approval" when record.EffectivenessNote.Trim().Length == 0 => "ต้องบันทึกผลการติดตามประสิทธิผลก่อน",
            _ => null,
        };
        if (missing is not null) return new IncidentResult(false, missing);

        // The last step is a person's signature, and only a supervisor may give
        // it. This is the line the AI cannot cross: it may draft the whole case
        // and it may not close one.
        if (next == "closed")
        {
            if (!StaffDirectory.IsSupervisor(role))
                return new IncidentResult(false, "ปิดเคสได้เฉพาะระดับหัวหน้างานขึ้นไป");
            record.ApprovedBy = by;
            record.ApprovedAt = DateTimeOffset.UtcNow;
        }

        record.Stage = next;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);

        return new IncidentResult(true, $"ไปยังขั้น {next}", id);
    }

    // Attaching evidence is an upload, not a row: POST /api/documents with a
    // caseId. It used to take an objectKey the caller invented, which meant the
    // one thing the storage structure depends on — that nobody composes their
    // own path — was left to whoever called it.

    private static IncidentView Describe(IncidentCase c, List<DocumentView> evidence)
    {
        var due = Formats.DateNumber(c.DueDate);

        var today = Formats.DateNumber(DateTimeOffset.Now.ToString("dd/MM/yyyy"));
        return new IncidentView(
            c.Id, c.Reference, c.JobKey, c.Kind, c.Category, c.Title, c.Stage,
            c.What, c.Where, c.When, c.Who, c.Why, c.How,
            c.AiSummary, c.RootCause, c.CorrectiveAction, c.PreventiveAction,
            c.ResponsiblePerson, c.DueDate, c.FollowUpNote, c.EffectivenessNote,
            c.ApprovedBy, c.ApprovedAt, c.RaisedBy, c.RaisedAt,
            c.Stage != "closed" && due > 0 && due < today,
            evidence,
            // Named, so the alignment is checkable by eye rather than by
            // counting commas against the record above.
            Company: c.Company, Grade: c.Grade, Source: c.Source, NcClause: c.NcClause,
            Team: c.Team, RequestedBy: c.RequestedBy, RequestedOn: c.RequestedOn,
            ImmediateAction: c.ImmediateAction, ImmediateBy: c.ImmediateBy,
            ImmediateDue: c.ImmediateDue,
            DocumentsToRevise: c.DocumentsToRevise, FollowUpBy: c.FollowUpBy,
            ReviewedBy: c.ReviewedBy, ApprovalOutcome: c.ApprovalOutcome,
            ApprovalNote: c.ApprovalNote, TeamNote: c.TeamNote);
    }
}
