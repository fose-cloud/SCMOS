using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Data;

/// <summary>Phase 2 assignment state, integrity, idempotency and tenant-isolation checks.</summary>
public static class CarrierCollaborationCheck
{
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-carrier-collaboration")) return null;

        var failed = 0;
        failed += Check("pending is an active assignment", CarrierAssignment.IsActive(CarrierAssignment.Pending), true);
        failed += Check("accepted is an active assignment", CarrierAssignment.IsActive(CarrierAssignment.Confirmed), true);
        failed += Check("rejected is closed", CarrierAssignment.IsClosed(CarrierAssignment.Rejected), true);
        failed += Check("superseded is closed", CarrierAssignment.IsClosed(CarrierAssignment.Superseded), true);

        failed += Check("pending accept applies once",
            CarrierAssignment.DecideAnswer(CarrierAssignment.Pending, CarrierAssignment.Confirmed),
            AssignmentAnswerDecision.Apply);
        failed += Check("repeated accept is idempotent",
            CarrierAssignment.DecideAnswer(CarrierAssignment.Confirmed, CarrierAssignment.Confirmed),
            AssignmentAnswerDecision.Replay);
        failed += Check("pending reject applies once",
            CarrierAssignment.DecideAnswer(CarrierAssignment.Pending, CarrierAssignment.Rejected),
            AssignmentAnswerDecision.Apply);
        failed += Check("repeated reject is idempotent",
            CarrierAssignment.DecideAnswer(CarrierAssignment.Rejected, CarrierAssignment.Rejected),
            AssignmentAnswerDecision.Replay);
        failed += Check("old carrier cannot accept a superseded assignment",
            CarrierAssignment.DecideAnswer(CarrierAssignment.Superseded, CarrierAssignment.Confirmed),
            AssignmentAnswerDecision.Refuse);

        var carrierA = new HashSet<string>(["CARRIER A", "A LOGISTICS"], StringComparer.OrdinalIgnoreCase);
        failed += Check("carrier owns an assignment with its stable supplier id",
            CarrierAssignment.BelongsTo(10, "different spelling", 10, carrierA), true);
        failed += Check("carrier A cannot see carrier B by known id even if its text resembles an alias",
            CarrierAssignment.BelongsTo(11, "A LOGISTICS", 10, carrierA), false);
        failed += Check("legacy assignment uses registered alias fallback",
            CarrierAssignment.BelongsTo(null, "a logistics", 10, carrierA), true);

        var options = new DbContextOptionsBuilder<ScmosDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=scmos-carrier-collaboration-model;Trusted_Connection=True")
            .Options;
        using var db = new ScmosDbContext(options);
        var assignment = db.Model.FindEntityType(typeof(SupplierRequest));
        var activeIndex = assignment?.GetIndexes().SingleOrDefault(index => index.GetDatabaseName()
            == "supplier_requests_one_active_job_idx");
        failed += Check("database enforces one active assignment per job", activeIndex?.IsUnique, true);
        failed += Check("one-active index covers job identity",
            activeIndex?.Properties.Select(property => property.Name).SequenceEqual([nameof(SupplierRequest.JobKey)]) == true,
            true);
        failed += Check("one-active index filters pending and confirmed",
            activeIndex?.GetFilter(), "[outcome] IN ('pending','confirmed')");
        failed += Check("reassignment history links to the previous assignment",
            assignment?.FindProperty(nameof(SupplierRequest.PreviousRequestId)) is not null, true);
        failed += Check("new assignments carry stable supplier identity",
            assignment?.FindProperty(nameof(SupplierRequest.SupplierId)) is not null, true);

        var accepted = new CarrierService.CarrierJob(
            "JOB-1", "2609001", "CUSTOMER", "BANGKOK", "1X20'", "", "", "",
            "25/09/2026", "08:00", JobStatus.SupplierConfirmed, 42, null, DateTimeOffset.UtcNow,
            "", "", "", AssignmentOutcome: CarrierAssignment.Confirmed, OperationalAvailable: true);
        var portal = new CarrierService.Portal(10, "Carrier A", [], [accepted], [accepted]);
        failed += Check("accepted assignment is projected into carrier schedule",
            portal.Schedule.Single().Key, "JOB-1");
        failed += Check("schedule is a projection, not another job identity",
            portal.Schedule.Single().Key == portal.Accepted.Single().Key, true);

        Console.WriteLine(failed == 0
            ? "Carrier Collaboration Phase 2 checks passed."
            : $"{failed} Carrier Collaboration Phase 2 check(s) failed.");
        return failed == 0 ? 0 : 1;
    }

    private static int Check<T>(string why, T got, T want)
    {
        var ok = EqualityComparer<T>.Default.Equals(got, want);
        Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {why}");
        if (!ok) Console.WriteLine($"       got {got}; want {want}");
        return ok ? 0 : 1;
    }
}
