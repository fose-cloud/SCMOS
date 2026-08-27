using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;

namespace Scmos.Api.Services;

/// <summary>One row of the issue log, as the screen draws it.</summary>
public record IssueView(
    long Id, string Code, string FoundOn, string FoundAt, string Source, string Reporter,
    string JobRef, string JobKey, string Detail, string Category, string Severity,
    string Impact, string Channel, string Owner, string OwnerId, string DueOn,
    string Status, string RootCause,
    /// <summary>Who was driving, what was on the lorry, and which lorry.</summary>
    string Driver, string ContainerNo, string Licence,
    /// <summary>Minor or Major on an accident; blank otherwise and blank when ungraded.</summary>
    string AccidentGrade,
    /// <summary>
    /// The column of the customer's carrier scorecard this counts under.
    ///
    /// Always answered: what somebody chose, or what the register implies when
    /// nobody has. The screen shows it on every row, because this is the field
    /// that moves a haulier's mark and it used to be invisible.
    /// </summary>
    string ScorecardColumn,
    /// <summary>The job it attached to, when it attached to one.</summary>
    string JobCustomer, string JobTrucker, string JobDate,
    /// <summary>Hours allowed for this severity, and whether it is past them.</summary>
    int SlaHours, bool Overdue);

/// <summary>The vocabularies the form may offer, served rather than duplicated.</summary>
public record IssueForm(
    IReadOnlyList<string> Sources, IReadOnlyList<string> Categories,
    IReadOnlyList<string> Severities, IReadOnlyList<string> Statuses,
    IReadOnlyList<string> Channels, IReadOnlyList<string> RootCauses,
    IReadOnlyList<string> Owners, IReadOnlyDictionary<string, int> Sla,
    /// <summary>
    /// The customer's own scorecard headings, served rather than written out in
    /// the browser as well — the form offers exactly what the scorecard counts.
    /// </summary>
    IReadOnlyList<string> ScorecardColumns,
    /// <summary>Which statuses mean the issue is finished with. The screen
    /// draws those differently and must not keep its own copy of the list.</summary>
    IReadOnlyList<string> Settled);

public record IssueSummary(int Total, int Outstanding, int Critical, int Overdue, int OnTime,
    IReadOnlyList<Counted> BySource, IReadOnlyList<Counted> ByStatus,
    IReadOnlyList<Counted> BySeverity, IReadOnlyList<Counted> ByCategory);

public record IssueResult(bool Ok, string Message, long? Id = null, string? Code = null);

/// <summary>
/// The daily record of what went wrong.
///
/// Every vocabulary here is the one the team already uses, taken from the
/// ตั้งค่า sheet of the workbook they keep. Served to the browser rather than
/// written out there as well, so a severity the form offers is a severity this
/// accepts — the alternative is two lists that agree until somebody edits one.
/// </summary>
public class OperationalIssueService(ScmosDbContext db)
{
    private static readonly string[] SourceList =
        ["CS", "Shipping", "CS/Shipping", "Billing", "Subcontractor", "Warehouse",
         "Customer", "Customs", "Freight forwarder", "Depot"];

    private static readonly string[] CategoryList =
        ["รถเข้ารับ/ส่งล่าช้า", "เอกสารขนส่ง/ศุลกากร", "ค่าขนส่ง/ใบแจ้งหนี้", "รถ/อุปกรณ์ไม่พร้อม",
         "พนักงานขับรถ/พฤติกรรม", "สินค้าชำรุด/สูญหาย", "POD/เอกสารส่งมอบ", "ความปลอดภัย/อุบัติเหตุ",
         "การสื่อสาร/ติดตามงาน", "Rent / Demurrage / Detention", "ความล่าช้าในการลงสินค้า",
         "ลานรับตู้เปล่า", "อื่น ๆ"];

    private static readonly string[] SeverityList = ["วิกฤต", "สูง", "ปานกลาง", "ต่ำ"];

    private static readonly string[] StatusList =
        ["เปิด", "กำลังดำเนินการ", "รอข้อมูล", "รออนุมัติ", "แก้ไขแล้ว", "ปิด", "ยกเลิก"];

    private static readonly string[] ChannelList =
        ["โทรศัพท์", "อีเมล", "Teams/Chat", "ระบบ/TMS", "ประชุม", "หน้างาน",
         "โทรศัพท์/Teams/Chat/Line", "โทรศัพท์/Line"];

    private static readonly string[] RootCauseList =
        ["เอกสาร/ข้อมูล/การสื่อสาร", "กระบวนการภายใน", "ผู้รับเหมาขนส่ง", "หน่วยงานต้นทาง",
         "หน่วยงานปลายทาง", "ข้อจำกัดเส้นทาง/จราจร", "เหตุสุดวิสัย", "หน้าที่ความรับผิดชอบไม่ชัดเจน"];

    /// <summary>
    /// Hours a severity is allowed before it is late, off the same sheet.
    ///
    /// The number is a target the team set, not a law of nature, and the screen
    /// says how many are past it rather than scoring anyone on it.
    /// </summary>
    private static readonly Dictionary<string, int> SlaHours = new()
    {
        ["วิกฤต"] = 4, ["สูง"] = 8, ["ปานกลาง"] = 24, ["ต่ำ"] = 48,
    };

    /// <summary>Statuses that mean the issue is no longer anybody's problem.</summary>
    private static readonly HashSet<string> Settled = ["แก้ไขแล้ว", "ปิด", "ยกเลิก"];

    public async Task<IssueForm> FormAsync(CancellationToken token)
    {
        var owners = await db.Staff.AsNoTracking()
            .Where(person => person.Active)
            .OrderBy(person => person.Name)
            .Select(person => person.Name)
            .ToListAsync(token);

        return new IssueForm(SourceList, CategoryList, SeverityList, StatusList,
            ChannelList, RootCauseList, owners, SlaHours,
            Rules.ScorecardColumn.All, [.. Settled]);
    }

    public async Task<IReadOnlyList<IssueView>> ListAsync(string? status, string? severity,
        string? jobKey, string? owner, CancellationToken token)
    {
        var query = db.OperationalIssues.AsNoTracking();

        var wantedStatus = (status ?? "").Trim();
        if (wantedStatus.Length > 0 && wantedStatus != "ALL")
        {
            // "ค้าง" is the question the screen opens with and it is not a
            // status anybody types — it is every status that is not settled.
            query = wantedStatus == "OUTSTANDING"
                ? query.Where(issue => !Settled.Contains(issue.Status))
                : query.Where(issue => issue.Status == wantedStatus);
        }

        var wantedSeverity = (severity ?? "").Trim();
        if (wantedSeverity.Length > 0 && wantedSeverity != "ALL")
            query = query.Where(issue => issue.Severity == wantedSeverity);

        var wantedJob = (jobKey ?? "").Trim();
        if (wantedJob.Length > 0) query = query.Where(issue => issue.JobKey == wantedJob);

        var wantedOwner = (owner ?? "").Trim();
        if (wantedOwner.Length > 0) query = query.Where(issue => issue.OwnerId == wantedOwner);

        var rows = await query.OrderByDescending(issue => issue.Id).ToListAsync(token);
        if (rows.Count == 0) return [];

        // The jobs these attach to, read once for the whole list rather than
        // once per row. Most of a day's issues land on a handful of jobs.
        var keys = rows.Select(issue => issue.JobKey).Where(key => key.Length > 0).Distinct().ToList();
        var jobs = keys.Count == 0
            ? []
            : await db.OperationJobs.AsNoTracking()
                .Where(job => keys.Contains(job.Key))
                .Select(job => new { job.Key, job.Customer, job.Trucker, job.WorkDate })
                .ToDictionaryAsync(job => job.Key, token);

        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7));
        return rows.Select(issue =>
        {
            jobs.TryGetValue(issue.JobKey, out var job);
            var hours = SlaHours.TryGetValue(issue.Severity, out var allowed) ? allowed : 0;
            return new IssueView(
                issue.Id, issue.Code, issue.FoundOn, issue.FoundAt, issue.Source, issue.Reporter,
                issue.JobRef, issue.JobKey, issue.Detail, issue.Category, issue.Severity,
                issue.Impact, issue.Channel, issue.Owner, issue.OwnerId, issue.DueOn,
                issue.Status, issue.RootCause,
                issue.Driver, issue.ContainerNo, issue.Licence, issue.AccidentGrade,
                Rules.ScorecardColumn.Of(issue),
                job?.Customer ?? "", job?.Trucker ?? "", job?.WorkDate ?? "",
                hours, IsOverdue(issue, hours, now));
        }).ToList();
    }

    /// <summary>
    /// Whether an issue has run past the hours its severity allows.
    ///
    /// Only counted for issues still open: an issue that was settled late is a
    /// fact about the past, and folding it into "how many are overdue right
    /// now" would make a number nobody can act on.
    /// </summary>
    private static bool IsOverdue(OperationalIssue issue, int hours, DateTimeOffset now)
    {
        if (hours == 0 || Settled.Contains(issue.Status)) return false;
        var found = ParseThaiDate(issue.FoundOn, issue.FoundAt);
        return found is not null && (now - found.Value).TotalHours > hours;
    }

    /// <summary>dd/MM/yyyy and HH:mm as the register writes them, or null.</summary>
    private static DateTimeOffset? ParseThaiDate(string date, string time)
    {
        var parts = (date ?? "").Split('/');
        if (parts.Length != 3) return null;
        if (!int.TryParse(parts[0], out var d) || !int.TryParse(parts[1], out var m)
            || !int.TryParse(parts[2], out var y)) return null;
        if (y < 1900 || m is < 1 or > 12 || d is < 1 or > 31) return null;

        var clock = (time ?? "").Split(':');
        var hh = clock.Length == 2 && int.TryParse(clock[0], out var h) ? h : 0;
        var mm = clock.Length == 2 && int.TryParse(clock[1], out var n) ? n : 0;
        if (hh is < 0 or > 23 || mm is < 0 or > 59) { hh = 0; mm = 0; }

        try
        {
            return new DateTimeOffset(y, m, d, hh, mm, 0, TimeSpan.FromHours(7));
        }
        catch (ArgumentOutOfRangeException)
        {
            // 31 February and the like. A date nobody can parse is not a date
            // this can judge lateness on, which is the honest answer.
            return null;
        }
    }

    public async Task<IssueSummary> SummaryAsync(CancellationToken token)
    {
        var rows = await db.OperationalIssues.AsNoTracking()
            .Select(issue => new { issue.Status, issue.Severity, issue.Source, issue.Category, issue.FoundOn, issue.FoundAt })
            .ToListAsync(token);

        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7));
        var outstanding = rows.Count(row => !Settled.Contains(row.Status));
        var overdue = rows.Count(row =>
        {
            if (Settled.Contains(row.Status)) return false;
            if (!SlaHours.TryGetValue(row.Severity, out var hours)) return false;
            var found = ParseThaiDate(row.FoundOn, row.FoundAt);
            return found is not null && (now - found.Value).TotalHours > hours;
        });

        return new IssueSummary(
            rows.Count, outstanding,
            rows.Count(row => row.Severity == "วิกฤต"),
            overdue, outstanding - overdue,
            Tally(rows.Select(row => row.Source)),
            Tally(rows.Select(row => row.Status)),
            Tally(rows.Select(row => row.Severity)),
            Tally(rows.Select(row => row.Category)));
    }

    private static IReadOnlyList<Counted> Tally(IEnumerable<string> values) =>
        values.Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value)
            .Select(group => new Counted(group.Key, group.Count()))
            .OrderByDescending(entry => entry.Value)
            .ToList();

    /// <summary>
    /// The job a written reference points at, or empty.
    ///
    /// References are typed by people under pressure: some carry two numbers
    /// separated by a slash, some are a booking, some a container. Each part is
    /// tried against the job code and the container, which are the two the
    /// register keeps as columns — the job code already carries the ABS number,
    /// because the importer falls back to it.
    ///
    /// No match is a normal outcome, not an error. An issue can be raised
    /// against a shipment that never became a job here.
    /// </summary>
    public async Task<string> ResolveJobKeyAsync(string jobRef, CancellationToken token)
    {
        var parts = (jobRef ?? "")
            .Split(['/', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => part.Length >= 6)
            .Select(part => part.ToUpperInvariant())
            .Distinct()
            .ToList();
        if (parts.Count == 0) return "";

        var found = await db.OperationJobs.AsNoTracking()
            .Where(job => parts.Contains(job.JobCode.ToUpper()) || parts.Contains(job.Container.ToUpper()))
            .Select(job => job.Key)
            .FirstOrDefaultAsync(token);
        return found ?? "";
    }

    /// <summary>
    /// The next OTL code.
    ///
    /// Read from the highest already issued rather than from a count, so
    /// deleting a row does not hand its number to the next issue — two
    /// different problems under one code is the kind of thing that gets noticed
    /// months later in a meeting.
    /// </summary>
    private async Task<string> NextCodeAsync(CancellationToken token)
    {
        var codes = await db.OperationalIssues.AsNoTracking()
            .Where(issue => issue.Code.StartsWith("OTL-"))
            .Select(issue => issue.Code)
            .ToListAsync(token);

        var highest = codes
            .Select(code => int.TryParse(code[4..], out var number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();
        return $"OTL-{highest + 1:D4}";
    }

    public async Task<IssueResult> RaiseAsync(OperationalIssue issue, string by, CancellationToken token)
    {
        if (issue.Detail.Trim().Length == 0)
            return new IssueResult(false, "ต้องกรอกรายละเอียดปัญหา");

        if (issue.Code.Trim().Length == 0) issue.Code = await NextCodeAsync(token);
        if (await db.OperationalIssues.AnyAsync(row => row.Code == issue.Code, token))
            return new IssueResult(false, $"รหัส {issue.Code} มีอยู่แล้ว");

        if (issue.JobKey.Length == 0 && issue.JobRef.Length > 0)
            issue.JobKey = await ResolveJobKeyAsync(issue.JobRef, token);

        if (issue.Status.Trim().Length == 0) issue.Status = "เปิด";
        if (issue.Severity.Trim().Length == 0) issue.Severity = "ปานกลาง";

        var now = DateTimeOffset.UtcNow;
        issue.CreatedBy = by;
        issue.CreatedAt = now;
        issue.UpdatedBy = by;
        issue.UpdatedAt = now;

        db.OperationalIssues.Add(issue);
        await db.SaveChangesAsync(token);
        // An issue can change what the register says about a job only when it
        // is raised from one; the cache holds jobs, not issues, so nothing to
        // invalidate here. Kept as a note so the next person does not go
        // looking for a missing call.
        return new IssueResult(true, $"บันทึกปัญหา {issue.Code} แล้ว", issue.Id, issue.Code);
    }

    public async Task<IssueResult> UpdateAsync(long id, Dictionary<string, string> fields,
        string by, CancellationToken token)
    {
        var issue = await db.OperationalIssues.FirstOrDefaultAsync(row => row.Id == id, token);
        if (issue is null) return new IssueResult(false, "ไม่พบปัญหานี้");

        foreach (var (name, value) in fields)
        {
            switch (name.ToLowerInvariant())
            {
                case "status": issue.Status = value.Trim(); break;
                case "severity": issue.Severity = value.Trim(); break;
                case "category": issue.Category = value.Trim(); break;
                case "source": issue.Source = value.Trim(); break;
                case "owner": issue.Owner = value.Trim(); break;
                case "ownerid": issue.OwnerId = value.Trim(); break;
                case "dueon": issue.DueOn = value.Trim(); break;
                case "rootcause": issue.RootCause = value.Trim(); break;
                case "impact": issue.Impact = value.Trim(); break;
                case "detail": issue.Detail = value.Trim(); break;
                case "channel": issue.Channel = value.Trim(); break;
                case "driver": issue.Driver = value.Trim(); break;
                case "containerno": issue.ContainerNo = value.Trim(); break;
                case "licence": issue.Licence = value.Trim(); break;
                case "accidentgrade": issue.AccidentGrade = value.Trim(); break;
                case "scorecardcolumn": issue.ScorecardColumn = value.Trim(); break;
                case "reporter": issue.Reporter = value.Trim(); break;
                case "jobref":
                    issue.JobRef = value.Trim();
                    issue.JobKey = await ResolveJobKeyAsync(issue.JobRef, token);
                    break;
                // Anything else is ignored rather than refused: the screen sends
                // what changed, and a field it does not own is not an error.
            }
        }

        issue.UpdatedBy = by;
        issue.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);
        return new IssueResult(true, "บันทึกแล้ว", issue.Id, issue.Code);
    }

    /// <summary>
    /// Adds many issues at once, as an import does.
    ///
    /// A code already in the table is skipped rather than replaced, so running
    /// the same sheet twice adds what is new and leaves what was already worked
    /// on alone. That matters: the log is edited here after it is imported, and
    /// re-importing must not undo somebody's afternoon.
    /// </summary>
    public async Task<(int Added, int Skipped)> ImportAsync(IReadOnlyList<OperationalIssue> issues,
        string by, CancellationToken token)
    {
        var existing = await db.OperationalIssues.AsNoTracking()
            .Select(issue => issue.Code)
            .ToListAsync(token);
        var known = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        var now = DateTimeOffset.UtcNow;
        var added = 0;
        var skipped = 0;

        foreach (var issue in issues)
        {
            if (issue.Code.Trim().Length == 0) issue.Code = await NextCodeAsync(token);
            if (!known.Add(issue.Code)) { skipped++; continue; }

            if (issue.JobKey.Length == 0 && issue.JobRef.Length > 0)
                issue.JobKey = await ResolveJobKeyAsync(issue.JobRef, token);
            if (issue.Status.Trim().Length == 0) issue.Status = "เปิด";

            issue.CreatedBy = by;
            issue.CreatedAt = now;
            issue.UpdatedBy = by;
            issue.UpdatedAt = now;
            db.OperationalIssues.Add(issue);
            added++;
        }

        if (added > 0) await db.SaveChangesAsync(token);
        return (added, skipped);
    }
}
