using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

/// <summary>
/// An operational issue and the CAR/PAR that answers it (1 Oct 2026): the dashboard's Accident and CAR/PAR cards follow
/// the period, and — on a throwaway LocalDB — a case opened from an issue is linked to it, an issue can be linked and
/// unlinked afterwards, each side lists the other, and deleting the case leaves the issue standing.
/// </summary>
static class IssueCaseLinkChecks
{
    public static async Task RunAsync(Action<bool, string> check, bool sql)
    {
        var september = new Period("2026", "09", "");
        IncidentCase Case(string reference, string jobKey, string requestedOn, DateTimeOffset raisedAt) =>
            new() { Reference = reference, JobKey = jobKey, RequestedOn = requestedOn, RaisedAt = raisedAt };
        var cases = new List<IncidentCase>
        {
            Case("ON-SEP-JOB", "J9", "", new DateTimeOffset(2026, 10, 5, 3, 0, 0, TimeSpan.Zero)),
            Case("ON-OCT-JOB", "J10", "15/09/2026", new DateTimeOffset(2026, 9, 15, 3, 0, 0, TimeSpan.Zero)),
            Case("ISSUED-SEP", "", "15/09/2026", new DateTimeOffset(2026, 10, 5, 3, 0, 0, TimeSpan.Zero)),
            Case("RAISED-1-OCT-BANGKOK", "", "", new DateTimeOffset(2026, 9, 30, 20, 0, 0, TimeSpan.Zero)),
            Case("JOB-GONE", "J-REMOVED", "", new DateTimeOffset(2026, 9, 10, 3, 0, 0, TimeSpan.Zero)),
        };
        var registered = new HashSet<string>(["J9", "J10"]);
        var inSeptember = new HashSet<string>(["J9"]);
        var shown = KpiEngine.CasesIn(september, registered, inSeptember, cases).Select(c => c.Reference).ToList();
        check(shown.SequenceEqual(["ON-SEP-JOB", "ISSUED-SEP", "JOB-GONE"])
            && KpiEngine.CasesIn(Period.All, registered, inSeptember, cases).Count == cases.Count,
            "dashboard: Accident and CAR/PAR follow the period — a case by its job's month, else the day it was issued, else raised (Bangkok)");
        if (sql) await SqlAsync(check);
    }

    private static async Task SqlAsync(Action<bool, string> check)
    {
        var database = "SCMOS_ISSUE_CASE_TEST_" + Guid.NewGuid().ToString("N");
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database={database};Integrated Security=true;TrustServerCertificate=true";
        var options = new DbContextOptionsBuilder<ScmosDbContext>().UseSqlServer(connection,
            s => { s.UseCompatibilityLevel(150); s.EnableRetryOnFailure(3); }).Options;
        await using var db = new ScmosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        try
        {
            var issues = new OperationalIssueService(db, new CarrierDirectory(db, new MemoryCache(new MemoryCacheOptions())));
            var incidents = new IncidentService(db);
            OperationalIssue Issue(string detail) => new()
            {
                FoundOn = "15/09/2026", FoundAt = "09:00", Source = "Customer", Category = "สินค้าชำรุด/สูญหาย", Severity = "สูง", Detail = detail,
            };
            var complaint = await issues.RaiseAsync(Issue("Pallet crushed at consignee"), "test", default);
            var second = await issues.RaiseAsync(Issue("Late again, customer called"), "test", default);

            var raised = await incidents.RaiseAsync("", "CAR", "damage", "Crushed pallet", "test", default, issueId: complaint.Id);
            var caseId = raised.Id!.Value;
            var linkedOnRaise = (await db.OperationalIssues.AsNoTracking().FirstAsync(row => row.Id == complaint.Id)).CaseId;

            // A case answers one issue (2 Oct 2026): the second issue is refused this case and takes a closed one instead.
            var other = await incidents.RaiseAsync("", "PAR", "delay", "Repeated delay", "test", default);
            var otherCase = await db.IncidentCases.FirstAsync(row => row.Id == other.Id);
            otherCase.Stage = "closed";
            await db.SaveChangesAsync();
            var linkableBefore = await incidents.LinkableAsync(default);
            var taken = await issues.LinkCaseAsync(second.Id!.Value, caseId, "test", default);
            var linked = await issues.LinkCaseAsync(second.Id.Value, other.Id!.Value, "test", default);
            var missing = await issues.LinkCaseAsync(second.Id.Value, 999_999, "test", default);
            var view = await incidents.ReadAsync(caseId, default);
            var log = await issues.ListAsync("ALL", null, null, null, default);
            check(raised.Ok && linkedOnRaise == caseId && !taken.Ok && taken.Message.Contains(complaint.Code!) && linked.Ok && !missing.Ok
                && linkableBefore.Select(one => one.Id).SequenceEqual([other.Id.Value])
                && (await incidents.LinkableAsync(default)).Count == 0
                && view!.Issues!.Single().Code == complaint.Code && view.Issues!.All(issue => issue.Column == ScorecardColumn.Complaint)
                && log.Single(row => row.CaseId == caseId).CaseReference == view.Reference
                && (await db.OperationalIssues.AsNoTracking().FirstAsync(row => row.Id == second.Id)).CaseId == other.Id,
                "issue ↔ CAR/PAR: a case opened from an issue is linked to it; any case of any stage can be linked later — but one already linked to another issue is neither offered nor taken");

            var unlinked = await issues.LinkCaseAsync(second.Id.Value, null, "test", default);
            var offeredAgain = await incidents.LinkableAsync(default);
            var removed = await incidents.DeleteAsync(caseId, Roles.Supervisor, default);
            var orphan = await db.OperationalIssues.AsNoTracking().FirstAsync(row => row.Id == complaint.Id);
            check(unlinked.Ok && offeredAgain.Select(one => one.Id).SequenceEqual([other.Id.Value]) && removed.Ok && orphan.CaseId is null
                && await db.OperationalIssues.CountAsync() == 2,
                "issue ↔ CAR/PAR: an unlinked case is offered again, and deleting the case unlinks its issues rather than deleting them");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
