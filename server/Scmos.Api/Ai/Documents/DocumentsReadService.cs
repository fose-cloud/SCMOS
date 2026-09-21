using System.Text.Json;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Ai.Documents;

/// <summary>Where the paperwork comes from — the register's analysis projection and the documents table, behind a boundary the checks can stand in for.</summary>
public interface IDocumentSource
{
    /// <summary>The register, projected as the Operations reads see it: no driver, plate, note or reason text — presence only.</summary>
    IAsyncEnumerable<OperationAnalysisRow> JobsAsync(OperationReadScope scope, CancellationToken token);
    /// <summary>The files filed under the given jobs.</summary>
    Task<IReadOnlyList<StoredDocument>> JobDocumentsAsync(IReadOnlyCollection<string> jobKeys, CancellationToken token);
    /// <summary>Every supplier's and driver's file that carries an expiry date — the compliance screen's own set.</summary>
    Task<IReadOnlyList<StoredDocument>> ComplianceAsync(CancellationToken token);
}

/// <summary>A job as the paperwork evidence names it, with what its checklist still wants.</summary>
public sealed record DocumentJob(string Key, string JobCode, string Customer, string Trucker, string OwnerId, string Category,
    string Date, string ArrDate, string Status, string Container,
    int Missing, int MissingBlocking, int Unclear, IReadOnlyList<string> MissingFolders);

/// <summary>
/// One row of paperwork evidence: a file that is held, a folder that is
/// empty, a job's invoice standing, or a compliance file near its expiry.
/// A job's driver, plate and notes are not here; a file's note appears only
/// as the "unreadable" mark a person left on it.
/// </summary>
public sealed record DocumentEvidence(
    /// <summary>doc:123 for a file; job:KEY for a job's standing; job:KEY:Folder for an empty folder.</summary>
    string Id,
    /// <summary>document · checklist · job.</summary>
    string Kind,
    string JobKey, string JobCode, string Customer, string Trucker, string Category, string Date, string Status,
    string Folder, string FileName, string DocKind, string UploadedBy, DateTimeOffset? UploadedAt,
    string ExpiryDate, int? DaysLeft,
    /// <summary>For a compliance file: the supplier's code, or the driver's name, as the file is filed.</summary>
    string Owner,
    /// <summary>held · unclear · missing · blocking · filed_in_time · filed_late · due · overdue · expiring · expired.</summary>
    string State, string Detail, string Source);

public sealed record DocumentsAnswer(string View, string AsOfDate, string TimeZone, string Window,
    int Total, int Returned, bool Truncated,
    int Held, int Missing, int Blocking, int Unclear,
    int InTime, int Late, int Due, int Overdue, int Expiring, int Expired,
    IReadOnlyList<DocumentJob> Jobs, DateTimeOffset RetrievedAt, string Rule, string Basis,
    IReadOnlyList<DocumentEvidence> Rows);

/// <summary>
/// The Document &amp; Invoice Agent's one read — Phase 5 (22 Sep 2026).
///
/// What each job still owes in paperwork, by the checklist the verification
/// screen already applies (<see cref="DocumentChecklist"/>); which done jobs
/// have their carrier's invoice filed and whether inside the billing KPI's
/// four days (<see cref="DocumentChecklist.InvoiceDays"/>); which suppliers'
/// and drivers' compliance files are near their expiry, by the window the
/// compliance screen watches (<see cref="DocumentService.ExpiringWithinDays"/>).
/// No file is opened and no model reads one: the rows are the register's and
/// the documents table's own facts. No amount is compared — the register
/// holds no invoice amount and no rate is keyed to a job — and nothing is
/// approved.
/// </summary>
public sealed class DocumentsReadService(IDocumentSource? source, TimeProvider clock)
{
    public const string Tool = "query_documents";
    public static readonly string[] Views = ["job", "missing", "invoice", "expiring"];
    public const int EvidenceLimit = 50;
    public const int MaxDays = 90;
    public const int DefaultDays = 14;
    /// <summary>The bell's horizon for paperwork wanted before a job runs: due within two days, or overdue.</summary>
    public const int AheadDays = 2;
    /// <summary>The rule and its figures, on every answer — the checklist, the billing KPI's days, the compliance window — each read from where it is written.</summary>
    public static readonly string RuleVersion = $"DocumentChecklist v1 · invoice within {DocumentChecklist.InvoiceDays} days · expiry watch {DocumentService.ExpiringWithinDays} days";

    /// <summary>Whether the tables stand behind this read.</summary>
    public bool Connected => source is not null;

    public async Task<DocumentsAnswer> ReadAsync(string tool, JsonElement arguments, AiToolContext context, CancellationToken token)
    {
        if (tool != Tool) throw new InvalidOperationException("Unknown read tool.");
        if (!context.Scope.Team && string.IsNullOrWhiteSpace(context.Scope.OperatorId))
            throw new UnauthorizedAccessException("An explicit operator scope is required.");
        if (source is null) throw new InvalidOperationException("No document source is connected.");
        var view = arguments.GetProperty("view").GetString() ?? "";
        var limit = arguments.GetProperty("limit").GetInt32();
        var days = arguments.TryGetProperty("days", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt32() : DefaultDays;
        var query = arguments.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String ? Clean(q.GetString(), 120) : "";
        if (!Views.Contains(view, StringComparer.Ordinal) || limit is < 1 or > EvidenceLimit || days is < 1 or > MaxDays)
            throw new InvalidOperationException("Invalid read arguments.");
        if (view == "job" && query.Length == 0) throw new InvalidOperationException("A job view needs a job.");

        var now = context.AsOf ?? clock.GetUtcNow();
        var bangkok = now.ToOffset(Formats.Zone);
        var today = DateOnly.FromDateTime(bangkok.DateTime);
        var scope = new OperationReadScope(context.Scope.Team, context.Scope.OperatorId);

        return view switch
        {
            "expiring" => await ExpiringAsync(limit, today, now, token),
            "job" => await JobAsync(query, limit, scope, today, now, token),
            "missing" => await MissingAsync(days, limit, scope, today, now, token),
            _ => await InvoiceAsync(days, limit, scope, today, now, token),
        };
    }

    /* ------------------------------------------------------------ jobs */

    private async Task<List<WorkspaceTabs.JobView>> CollectAsync(OperationReadScope scope, Func<WorkspaceTabs.JobView, bool> wanted, CancellationToken token)
    {
        var jobs = new List<WorkspaceTabs.JobView>();
        await foreach (var row in source!.JobsAsync(scope, token))
        {
            if (row.Job is not { } job || job.Key.Length == 0) continue;
            // Cancelled jobs owe nothing; closed ones still owe their POD and invoice.
            if (WorkspaceTabs.IsCancelled(job)) continue;
            if (wanted(job)) jobs.Add(job);
        }
        return jobs;
    }

    private static bool Matches(WorkspaceTabs.JobView job, string query) =>
        string.Equals(job.Key, query, StringComparison.OrdinalIgnoreCase)
        || (job.JobCode.Length > 0 && job.JobCode.Contains(query, StringComparison.OrdinalIgnoreCase))
        || (job.Container.Length > 0 && job.Container.Contains(query, StringComparison.OrdinalIgnoreCase))
        || (job.Customer.Length > 0 && job.Customer.Contains(query, StringComparison.OrdinalIgnoreCase));

    private static bool Within(WorkspaceTabs.JobView job, DateOnly from, DateOnly to)
        => Formats.ParseDay(job.Date) is { } day && day >= from && day <= to;

    /// <summary>The day a job was done: the arrival the register recorded, else its plan date.</summary>
    public static (DateOnly? Day, string Basis) DoneOn(WorkspaceTabs.JobView job) =>
        Formats.ParseDay(job.ArrDate) is { } arrived ? (arrived, "arrival") : (Formats.ParseDay(job.Date), "plan");

    /// <summary>A job's checklist standing, by the verification screen's own rule.</summary>
    public static DocumentJob Standing(WorkspaceTabs.JobView job, IReadOnlyList<StoredDocument> held)
    {
        var missing = new List<string>();
        var blocking = 0;
        var unclear = 0;
        foreach (var wanted in DocumentChecklist.For(job.Cat))
        {
            var files = held.Where(file => file.Folder == wanted.Folder).ToList();
            if (files.Any(file => file.Note.Contains(VerificationService.UnclearMark))) unclear++;
            if (files.Count > 0 || !DocumentChecklist.ExpectedNow(wanted, job.Status)) continue;
            missing.Add(wanted.Folder);
            if (wanted.Blocking) blocking++;
        }
        return new DocumentJob(job.Key, job.JobCode, job.Customer, job.Trucker, job.OwnerId, job.Cat,
            job.Date, job.ArrDate, job.Status, job.Container, missing.Count, blocking, unclear, missing);
    }

    private async Task<Dictionary<string, List<StoredDocument>>> FilesOfAsync(IReadOnlyCollection<string> keys, CancellationToken token)
    {
        if (keys.Count == 0) return new(StringComparer.Ordinal);
        return (await source!.JobDocumentsAsync(keys, token))
            .GroupBy(file => file.JobKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
    }

    /* ------------------------------------------------------------ views */

    private async Task<DocumentsAnswer> JobAsync(string query, int limit, OperationReadScope scope, DateOnly today, DateTimeOffset now, CancellationToken token)
    {
        var jobs = (await CollectAsync(scope, job => Matches(job, query), token))
            .OrderByDescending(job => Formats.DateNumber(job.Date)).Take(5).ToList();
        var files = await FilesOfAsync(jobs.Select(job => job.Key).ToList(), token);
        var standings = jobs.Select(job => Standing(job, files.GetValueOrDefault(job.Key, []))).ToList();
        var rows = new List<DocumentEvidence>();
        int held = 0, unclear = 0;
        foreach (var job in jobs)
        {
            var mine = files.GetValueOrDefault(job.Key, []);
            foreach (var file in mine.OrderByDescending(file => file.UploadedAt))
            {
                var isUnclear = file.Note.Contains(VerificationService.UnclearMark);
                held++;
                if (isUnclear) unclear++;
                rows.Add(File(job, file, isUnclear ? "unclear" : "held",
                    isUnclear ? "มีคนทำเครื่องหมายว่าอ่านไม่ชัด — " + Clean(file.Note, 120) : $"อยู่ในโฟลเดอร์ {file.Folder}"));
            }
            foreach (var wanted in DocumentChecklist.For(job.Cat))
            {
                if (mine.Any(file => file.Folder == wanted.Folder) || !DocumentChecklist.ExpectedNow(wanted, job.Status)) continue;
                rows.Add(new DocumentEvidence($"job:{job.Key}:{wanted.Folder}", "checklist", job.Key, job.JobCode, job.Customer, job.Trucker, job.Cat,
                    job.Date, job.Status, wanted.Folder, "", wanted.Thai, "", null, "", null, "",
                    wanted.Blocking ? "blocking" : "missing",
                    (wanted.Blocking ? "ยังไม่มี (หยุดงานได้): " : "ยังไม่มี: ") + wanted.Why, "documents"));
            }
        }
        var total = rows.Count;
        var shown = rows.Take(limit).ToList();
        return new DocumentsAnswer("job", Formats.PlanDate(today), "Asia/Bangkok", "all_dates_for_the_job",
            total, shown.Count, total > shown.Count,
            held, standings.Sum(job => job.Missing), standings.Sum(job => job.MissingBlocking), unclear,
            0, 0, 0, 0, 0, 0, standings, now, RuleVersion, Basis, shown);
    }

    private async Task<DocumentsAnswer> MissingAsync(int days, int limit, OperationReadScope scope, DateOnly today, DateTimeOffset now, CancellationToken token)
    {
        var from = today.AddDays(-days);
        var to = today.AddDays(AheadDays);
        var jobs = await CollectAsync(scope, job => Within(job, from, to), token);
        var files = await FilesOfAsync(jobs.Select(job => job.Key).ToList(), token);
        var standings = jobs.Select(job => Standing(job, files.GetValueOrDefault(job.Key, [])))
            .Where(job => job.Missing > 0 || job.Unclear > 0)
            // Blocking first, then the most incomplete, then the earliest — the
            // job a truck is waiting on outranks the one missing a photo.
            .OrderByDescending(job => job.MissingBlocking).ThenByDescending(job => job.Missing).ThenBy(job => Formats.DateNumber(job.Date))
            .ToList();
        var rows = standings.Select(job => new DocumentEvidence($"job:{job.Key}", "job", job.Key, job.JobCode, job.Customer, job.Trucker, job.Category,
            job.Date, job.Status, string.Join(",", job.MissingFolders), "", "", "", null, "", null, "",
            job.MissingBlocking > 0 ? "blocking" : job.Missing > 0 ? "missing" : "unclear",
            (job.Missing > 0 ? "ยังไม่มี " + string.Join(" · ", job.MissingFolders.Select(ThaiFolder)) : "")
            + (job.Missing > 0 && job.Unclear > 0 ? " · " : "") + (job.Unclear > 0 ? $"อ่านไม่ชัด {job.Unclear} ไฟล์" : ""), "documents")).ToList();
        var total = rows.Count;
        var shown = rows.Take(limit).ToList();
        return new DocumentsAnswer("missing", Formats.PlanDate(today), "Asia/Bangkok", $"plan_date_last_{days}_days_to_{AheadDays}_ahead",
            total, shown.Count, total > shown.Count,
            jobs.Count - standings.Count(job => job.Missing > 0), standings.Count(job => job.Missing > 0), standings.Count(job => job.MissingBlocking > 0),
            standings.Count(job => job.Unclear > 0),
            0, 0, 0, 0, 0, 0, standings.Take(limit).ToList(), now, RuleVersion, Basis, shown);
    }

    private async Task<DocumentsAnswer> InvoiceAsync(int days, int limit, OperationReadScope scope, DateOnly today, DateTimeOffset now, CancellationToken token)
    {
        var from = today.AddDays(-days);
        var jobs = (await CollectAsync(scope, job => JobRules.IsDone(job.Status), token))
            .Select(job => (Job: job, Done: DoneOn(job)))
            .Where(pair => pair.Done.Day is { } day && day >= from && day <= today)
            .ToList();
        var files = await FilesOfAsync(jobs.Select(pair => pair.Job.Key).ToList(), token);
        var rows = new List<DocumentEvidence>();
        int inTime = 0, late = 0, due = 0, overdue = 0;
        foreach (var (job, done) in jobs)
        {
            var doneOn = done.Day!.Value;
            var invoice = files.GetValueOrDefault(job.Key, []).Where(file => file.Folder == "Invoice").OrderBy(file => file.UploadedAt).FirstOrDefault();
            var where = done.Basis == "arrival" ? "วันที่รถถึง" : "วันที่ตามแผน";
            if (invoice is not null)
            {
                var filedOn = DateOnly.FromDateTime(invoice.UploadedAt.ToOffset(Formats.Zone).DateTime);
                var after = filedOn.DayNumber - doneOn.DayNumber;
                var onTime = after <= DocumentChecklist.InvoiceDays;
                if (onTime) inTime++; else late++;
                rows.Add(File(job, invoice, onTime ? "filed_in_time" : "filed_late",
                    $"ใบแจ้งหนี้ถูกจัดเก็บ {Formats.PlanDate(filedOn)} — {Math.Max(after, 0)} วันหลังงานเสร็จ ({where} {Formats.PlanDate(doneOn)})"
                    + (onTime ? "" : $" เกินกำหนด {DocumentChecklist.InvoiceDays} วัน")));
                continue;
            }
            var since = today.DayNumber - doneOn.DayNumber;
            var isDue = since <= DocumentChecklist.InvoiceDays;
            if (isDue) due++; else overdue++;
            rows.Add(new DocumentEvidence($"job:{job.Key}", "job", job.Key, job.JobCode, job.Customer, job.Trucker, job.Cat,
                job.Date, job.Status, "Invoice", "", "", "", null, "", null, "",
                isDue ? "due" : "overdue",
                $"ยังไม่มีใบแจ้งหนี้ — งานเสร็จ {Formats.PlanDate(doneOn)} ({where}) ผ่านมา {since} วัน"
                + (isDue ? $" ยังอยู่ในกำหนด {DocumentChecklist.InvoiceDays} วัน" : $" เกินกำหนด {DocumentChecklist.InvoiceDays} วันแล้ว"), "documents"));
        }
        // The ones furthest past the rule first; then the newest.
        rows.Sort((a, b) => Rank(a.State).CompareTo(Rank(b.State)) != 0 ? Rank(a.State).CompareTo(Rank(b.State)) : Formats.DateNumber(b.Date).CompareTo(Formats.DateNumber(a.Date)));
        var total = rows.Count;
        var shown = rows.Take(limit).ToList();
        var standings = jobs.Select(pair => Standing(pair.Job, files.GetValueOrDefault(pair.Job.Key, []))).Take(limit).ToList();
        return new DocumentsAnswer("invoice", Formats.PlanDate(today), "Asia/Bangkok", $"done_last_{days}_days",
            total, shown.Count, total > shown.Count,
            inTime + late, due + overdue, 0, 0,
            inTime, late, due, overdue, 0, 0, standings, now, RuleVersion,
            Basis + "; an invoice's date is the day its file was filed, not a ledger date; no amount is compared; nothing is approved", shown);

        static int Rank(string state) => state switch { "overdue" => 0, "filed_late" => 1, "due" => 2, _ => 3 };
    }

    private async Task<DocumentsAnswer> ExpiringAsync(int limit, DateOnly today, DateTimeOffset now, CancellationToken token)
    {
        var horizon = today.AddDays(DocumentService.ExpiringWithinDays);
        var rows = new List<DocumentEvidence>();
        int expiring = 0, expired = 0;
        foreach (var file in await source!.ComplianceAsync(token))
        {
            if (Formats.ParseDay(file.ExpiryDate) is not { } expiry || expiry > horizon) continue;
            var left = expiry.DayNumber - today.DayNumber;
            var isExpired = left < 0;
            if (isExpired) expired++; else expiring++;
            var who = file.DriverId is not null ? "คนขับ " : "ผู้ขนส่ง ";
            rows.Add(new DocumentEvidence($"doc:{file.Id}", "document", "", "", "", "", "", "", "",
                file.Folder, Clean(file.FileName, 120), Clean(file.Kind, 60), Clean(file.UploadedBy, 80), file.UploadedAt,
                file.ExpiryDate, left, Clean(file.Customer, 80),
                isExpired ? "expired" : "expiring",
                who + Clean(file.Customer, 80) + (isExpired ? $" — หมดอายุแล้ว {-left} วัน ({file.ExpiryDate})" : $" — หมดอายุใน {left} วัน ({file.ExpiryDate})"), "documents"));
        }
        rows.Sort((a, b) => (a.DaysLeft ?? 0).CompareTo(b.DaysLeft ?? 0));
        var total = rows.Count;
        var shown = rows.Take(limit).ToList();
        return new DocumentsAnswer("expiring", Formats.PlanDate(today), "Asia/Bangkok", $"expiry_within_{DocumentService.ExpiringWithinDays}_days_or_expired",
            total, shown.Count, total > shown.Count,
            0, 0, 0, 0, 0, 0, 0, 0, expiring, expired, [], now, RuleVersion, Basis, shown);
    }

    private static DocumentEvidence File(WorkspaceTabs.JobView job, StoredDocument file, string state, string detail) =>
        new($"doc:{file.Id}", "document", job.Key, job.JobCode, job.Customer, job.Trucker, job.Cat, job.Date, job.Status,
            file.Folder, Clean(file.FileName, 120), Clean(file.Kind, 60), Clean(file.UploadedBy, 80), file.UploadedAt,
            file.ExpiryDate, null, "", state, detail, "documents");

    private const string Basis = "DocumentChecklist and VerificationService rules over operation_jobs and documents as stored; no file opened, no model reads one; driver, plate and note text omitted";

    public static string ThaiFolder(string folder) => folder switch
    {
        "Booking" => "ใบจอง/DO", "ECard" => "E-Card", "POD" => "ใบรับของ", "Images" => "รูปหน้างาน", "Invoice" => "ใบแจ้งหนี้", "CARPAR" => "CAR/PAR", _ => folder,
    };

    private static string Clean(string? text, int max)
    {
        var value = new string((text ?? "").Select(c => c is '\n' or '\r' or '\t' ? ' ' : c).Where(c => !char.IsControl(c)).ToArray());
        while (value.Contains("  ")) value = value.Replace("  ", " ");
        value = value.Trim();
        return value.Length > max ? value[..max] : value;
    }
}

public sealed class DocumentsReadHandler(DocumentsReadService service) : IAiReadToolHandler
{
    public async Task<JsonElement> ReadAsync(JsonElement arguments, AiToolContext context, CancellationToken token)
        => JsonSerializer.SerializeToElement(await service.ReadAsync(DocumentsReadService.Tool, arguments, context, token));
}
