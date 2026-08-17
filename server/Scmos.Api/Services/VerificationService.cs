using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record ChecklistItem(
    string Folder, string English, string Thai, bool Blocking, string Why,
    bool ExpectedNow, int Count, bool Unclear,
    IReadOnlyList<DocumentView> Files);

public record VerificationJob(
    string JobKey, string Reference, string Customer, string Carrier, string Category,
    string Date, string Status, string Container,
    /// <summary>The container number fails its own format, so it will not match the card.</summary>
    bool ContainerSuspect,
    int Missing, int MissingBlocking, int UnclearCount,
    IReadOnlyList<ChecklistItem> Checklist);

public record VerificationBoard(
    int Total, int Clear, int Blocked, int Unclear,
    IReadOnlyList<VerificationJob> Jobs);

public record VerificationResult(bool Ok, string Message);

/// <summary>
/// What each job still owes in paperwork.
///
/// This is the screen the workflow's document gate has always implied and never
/// had: the stage that says "check the B/L and send the documents" could hold a
/// job, but nobody could see across the plan which jobs were short of what.
///
/// Nothing here judges a file's contents — the system cannot read a PDF and know
/// the B/L matches. What it can do is say which of the required folders are
/// empty, which container numbers will fail at the gate, and which files a
/// person has looked at and marked unreadable. All three are facts.
/// </summary>
public class VerificationService(ScmosDbContext db)
{
    /// <summary>The marker a person leaves on a file they cannot read.</summary>
    public const string UnclearMark = "ไม่ชัด";

    public async Task<VerificationBoard> ReadAsync(string? scope, string? ownerId, CancellationToken token)
    {
        var rows = await db.OperationJobs.AsNoTracking().Select(job => job.Data).ToListAsync(token);
        var jobs = rows.Select(JobRecord.From).OfType<JobRecord>().ToList();

        if (!string.IsNullOrWhiteSpace(ownerId))
            jobs = jobs.Where(job => job.OpId == ownerId).ToList();

        // Closed jobs still owe their POD and invoice, so they stay in scope
        // until the paperwork is in. Cancelled ones do not.
        jobs = jobs.Where(job => JobStatus.FromLegacy(job.Status) != JobStatus.Cancelled).ToList();

        var keys = jobs.Select(job => job.Identity).ToHashSet(StringComparer.Ordinal);
        var documents = (await db.Documents.AsNoTracking()
                .Where(document => document.JobKey != "").ToListAsync(token))
            .Where(document => keys.Contains(document.JobKey))
            .GroupBy(document => document.JobKey)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var described = jobs.Select(job => Describe(job, documents.GetValueOrDefault(job.Identity, []))).ToList();

        var filtered = (scope ?? "").Trim().ToLowerInvariant() switch
        {
            "blocked" => described.Where(job => job.MissingBlocking > 0).ToList(),
            "unclear" => described.Where(job => job.UnclearCount > 0).ToList(),
            "clear" => described.Where(job => job.Missing == 0).ToList(),
            _ => described.Where(job => job.Missing > 0 || job.UnclearCount > 0).ToList(),
        };

        return new VerificationBoard(
            described.Count,
            described.Count(job => job.Missing == 0),
            described.Count(job => job.MissingBlocking > 0),
            described.Count(job => job.UnclearCount > 0),
            // Blocking first, then the most incomplete: the job a truck is
            // waiting on outranks the one missing a photo.
            filtered.OrderByDescending(job => job.MissingBlocking)
                .ThenByDescending(job => job.Missing)
                .ThenBy(job => Sortable(job.Date))
                .Take(300).ToList());
    }

    private static VerificationJob Describe(JobRecord job, List<StoredDocument> documents)
    {
        var required = DocumentChecklist.For(job.Cat);
        var checklist = new List<ChecklistItem>();
        var missing = 0;
        var missingBlocking = 0;
        var unclear = 0;

        foreach (var wanted in required)
        {
            var held = documents.Where(document => document.Folder == wanted.Folder).ToList();
            var expected = DocumentChecklist.ExpectedNow(wanted, job.Status);
            var isUnclear = held.Any(document => document.Note.Contains(UnclearMark));

            if (expected && held.Count == 0)
            {
                missing++;
                if (wanted.Blocking) missingBlocking++;
            }
            if (isUnclear) unclear++;

            checklist.Add(new ChecklistItem(
                wanted.Folder, wanted.English, wanted.Thai, wanted.Blocking, wanted.Why,
                expected, held.Count, isUnclear,
                held.Select(DocumentService.Describe).ToList()));
        }

        return new VerificationJob(
            job.Identity, job.Reference, job.Customer, job.Trucker, job.Cat,
            job.Date, job.Status, job.Container,
            Notifications.ContainerWillNotMatch(job),
            missing, missingBlocking, unclear, checklist);
    }

    /// <summary>
    /// Records that a person could not read a file.
    ///
    /// Written onto the document's own note, in their words, because the person
    /// who has to send a replacement needs to know what was unreadable — "ไม่ชัด"
    /// on its own gets a second copy of the same bad scan.
    /// </summary>
    public async Task<VerificationResult> MarkUnclearAsync(long documentId, string detail,
        CancellationToken token)
    {
        var document = await db.Documents.FirstOrDefaultAsync(row => row.Id == documentId, token);
        if (document is null) return new VerificationResult(false, "ไม่พบไฟล์นี้");

        var words = detail.Trim();
        if (words.Length == 0) return new VerificationResult(false, "ต้องระบุว่าอ่านส่วนไหนไม่ออก");

        document.Note = $"{UnclearMark}: {words}";
        await db.SaveChangesAsync(token);
        return new VerificationResult(true, $"แจ้งว่า {document.FileName} อ่านไม่ชัดแล้ว");
    }

    /// <summary>Clears the mark once a readable copy has arrived.</summary>
    public async Task<VerificationResult> ClearUnclearAsync(long documentId, CancellationToken token)
    {
        var document = await db.Documents.FirstOrDefaultAsync(row => row.Id == documentId, token);
        if (document is null) return new VerificationResult(false, "ไม่พบไฟล์นี้");
        document.Note = "";
        await db.SaveChangesAsync(token);
        return new VerificationResult(true, $"ยกเลิกการแจ้งของ {document.FileName} แล้ว");
    }

    private static int Sortable(string date)
    {
        var number = Formats.DateNumber(date);
        return number == 0 ? int.MaxValue : number;
    }
}
