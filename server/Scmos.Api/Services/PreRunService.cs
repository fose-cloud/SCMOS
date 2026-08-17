using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public class PreRunOptions
{
    public const string Section = "PreRun";

    /// <summary>Minutes a carrier has to answer the pre-run list.</summary>
    public int SlaMinutes { get; set; } = PreRun.DefaultSlaMinutes;
}

/// <summary>A job on tomorrow's list, with whatever has been asked about it so far.</summary>
public record PreRunLine(
    string JobKey,
    string Reference,
    string Customer,
    string Destination,
    string Carrier,
    string Type,
    string PlanTime,
    string PlannedTruck,
    string PlannedDriver,
    PreRunStatus? Check);

public record PreRunStatus(
    long Id,
    DateTimeOffset SentAt,
    string SentBy,
    DateTimeOffset? RespondedAt,
    int? ResponseMinutes,
    bool? MetSla,
    bool IsReady,
    string Outcome,
    string Escalation,
    string EscalationLabel,
    string? NextStep,
    string ConfirmedBy,
    string TruckNo,
    string Driver,
    string DriverContact,
    string Correction,
    string Remark);

public record PreRunList(
    string ShipmentDate,
    int SlaMinutes,
    int Total,
    int NotSent,
    int Awaiting,
    int Ready,
    int Breached,
    IReadOnlyList<PreRunLine> Lines);

public record PreRunResult(bool Ok, string Message);

/// <summary>
/// The day-before check.
///
/// The list is generated from the plan rather than kept as its own register:
/// tomorrow's shipments are whichever jobs carry tomorrow's date and a carrier,
/// so a job moved or reassigned during the day is on the right list without
/// anybody maintaining a second copy of it.
/// </summary>
public class PreRunService(ScmosDbContext db, IOptions<PreRunOptions> options)
{
    private readonly int _sla = options.Value.SlaMinutes > 0
        ? options.Value.SlaMinutes
        : PreRun.DefaultSlaMinutes;

    /// <summary>
    /// Tomorrow's shipments, or any date asked for.
    ///
    /// The plan writes dates DD/MM/YYYY, which is how they are stored, so the
    /// match is on that text rather than on a parsed date — the same string the
    /// operator sees on the grid.
    /// </summary>
    public async Task<PreRunList> BuildAsync(string shipmentDate, CancellationToken token)
    {
        var jobs = await db.OperationJobs.AsNoTracking()
            .Where(job => job.WorkDate == shipmentDate && job.Trucker != "")
            .OrderBy(job => job.Customer)
            .Select(job => new { job.Key, job.Customer, job.Trucker, job.JobCode, job.Data })
            .ToListAsync(token);

        var keys = jobs.Select(job => job.Key).ToList();
        var checks = await db.PreRunChecks.AsNoTracking()
            .Where(check => keys.Contains(check.JobKey))
            .ToListAsync(token);

        var now = DateTimeOffset.UtcNow;
        var lines = new List<PreRunLine>(jobs.Count);

        foreach (var job in jobs)
        {
            var record = JobRecord.From(job.Data);
            // The newest check for this job is the one that counts; earlier ones
            // are re-sends and stay in the table for the history.
            var check = checks.Where(c => c.JobKey == job.Key).OrderByDescending(c => c.Id).FirstOrDefault();

            lines.Add(new PreRunLine(
                job.Key,
                record?.Reference ?? job.JobCode,
                job.Customer,
                record?.Cat == "EXPORT" ? "" : "",
                job.Trucker,
                record?.Type ?? "",
                record?.PlanTime ?? "",
                record?.Licence ?? "",
                record?.Driver ?? "",
                check is null ? null : Describe(check, now)));
        }

        return new PreRunList(
            shipmentDate,
            _sla,
            lines.Count,
            lines.Count(line => line.Check is null),
            lines.Count(line => line.Check?.Outcome == "pending"),
            lines.Count(line => line.Check?.IsReady == true),
            lines.Count(line => line.Check?.MetSla == false),
            lines);
    }

    /// <summary>Sends the list for one job. Re-sending closes the previous open check.</summary>
    public async Task<PreRunResult> SendAsync(string jobKey, string by, CancellationToken token)
    {
        var job = await db.OperationJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Key == jobKey, token);
        if (job is null) return new PreRunResult(false, "ไม่พบงานนี้");
        if (job.Trucker.Trim().Length == 0)
            return new PreRunResult(false, "งานนี้ยังไม่มีผู้ขนส่ง — ต้องมอบหมายก่อนส่ง pre-run");

        var open = await db.PreRunChecks
            .Where(c => c.JobKey == jobKey && c.Outcome == "pending")
            .ToListAsync(token);

        foreach (var previous in open)
        {
            // A re-send supersedes the earlier one rather than racing it. The old
            // row is closed as unanswered so the SLA count stays honest.
            previous.Outcome = "no-response";
            previous.ResponseMinutes = PreRun.MinutesTaken(previous.SentAt, null, DateTimeOffset.UtcNow);
        }

        var record = JobRecord.From(job.Data);
        db.PreRunChecks.Add(new PreRunCheck
        {
            JobKey = jobKey,
            ShipmentDate = job.WorkDate,
            Carrier = job.Trucker,
            SentAt = DateTimeOffset.UtcNow,
            SentBy = by,
            TruckNo = record?.Licence ?? "",
            Driver = record?.Driver ?? "",
            DriverContact = record?.Contact ?? "",
            Outcome = "pending",
            Escalation = "none",
        });

        await db.SaveChangesAsync(token);
        return new PreRunResult(true, open.Count > 0 ? "ส่ง pre-run ใหม่แล้ว (ฉบับก่อนปิดเป็นไม่ตอบ)" : "ส่ง pre-run แล้ว");
    }

    /// <summary>
    /// Records what the carrier said. A correction is written onto the job as
    /// well as the check — the plate they name is the plate that will arrive,
    /// and the grid has to show it.
    /// </summary>
    public async Task<PreRunResult> RespondAsync(long id, string confirmedBy, string truckNo, string driver,
        string driverContact, string correction, string remark, string by, CancellationToken token)
    {
        var check = await db.PreRunChecks.FirstOrDefaultAsync(c => c.Id == id, token);
        if (check is null) return new PreRunResult(false, "ไม่พบรายการ pre-run นี้");
        if (check.Outcome != "pending") return new PreRunResult(false, "รายการนี้บันทึกผลไปแล้ว");

        var now = DateTimeOffset.UtcNow;
        var newTruck = truckNo.Trim();
        var newDriver = driver.Trim();
        var newContact = driverContact.Trim();

        var changed = (newTruck.Length > 0 && newTruck != check.TruckNo)
                      || (newDriver.Length > 0 && newDriver != check.Driver)
                      || (newContact.Length > 0 && newContact != check.DriverContact)
                      || correction.Trim().Length > 0;

        check.RespondedAt = now;
        check.ResponseMinutes = PreRun.MinutesTaken(check.SentAt, now, now);
        check.ConfirmedBy = confirmedBy.Trim();
        check.Correction = correction.Trim();
        check.Remark = remark.Trim();
        check.Outcome = changed ? "corrected" : "confirmed";
        if (newTruck.Length > 0) check.TruckNo = newTruck;
        if (newDriver.Length > 0) check.Driver = newDriver;
        if (newContact.Length > 0) check.DriverContact = newContact;

        if (changed) await ApplyToJob(check, by, token);

        await db.SaveChangesAsync(token);
        var met = PreRun.MetSla(check.SentAt, check.RespondedAt, now, _sla);
        return new PreRunResult(true,
            (changed ? "บันทึกการแก้ไขจากผู้ขนส่งแล้ว" : "ผู้ขนส่งยืนยันแล้ว") +
            $" · ตอบใน {check.ResponseMinutes} นาที" + (met == false ? " (เกิน SLA)" : ""));
    }

    /// <summary>Moves the chase one step: follow-up, then escalation.</summary>
    public async Task<PreRunResult> ChaseAsync(long id, string note, string by, CancellationToken token)
    {
        var check = await db.PreRunChecks.FirstOrDefaultAsync(c => c.Id == id, token);
        if (check is null) return new PreRunResult(false, "ไม่พบรายการ pre-run นี้");

        var outcome = Enum.Parse<PreRunOutcome>(ToEnumName(check.Outcome), true);
        var current = Enum.Parse<Escalation>(ToEnumName(check.Escalation), true);
        var next = PreRun.NextStep(current, outcome);
        if (next is null) return new PreRunResult(false, "รายการนี้ไม่ต้องตามแล้ว");

        check.Escalation = ToWireName(next.Value);
        if (note.Trim().Length > 0)
            check.Remark = (check.Remark.Length > 0 ? check.Remark + " · " : "") + $"{by}: {note.Trim()}";

        await db.SaveChangesAsync(token);
        return new PreRunResult(true, PreRun.Describe(next.Value));
    }

    /* --------------------------------------------------------------- inside */

    private async Task ApplyToJob(PreRunCheck check, string by, CancellationToken token)
    {
        var job = await db.OperationJobs.FirstOrDefaultAsync(j => j.Key == check.JobKey, token);
        if (job is null) return;

        var node = System.Text.Json.Nodes.JsonNode.Parse(job.Data)?.AsObject();
        if (node is null) return;

        if (check.TruckNo.Length > 0) node["licence"] = check.TruckNo;
        if (check.Driver.Length > 0) node["driver"] = check.Driver;
        if (check.DriverContact.Length > 0) node["contact"] = check.DriverContact;

        job.Data = node.ToJsonString();
        job.UpdatedBy = by;
        job.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private PreRunStatus Describe(PreRunCheck check, DateTimeOffset now)
    {
        var outcome = Enum.Parse<PreRunOutcome>(ToEnumName(check.Outcome), true);
        var met = PreRun.MetSla(check.SentAt, check.RespondedAt, now, _sla);
        var escalation = Enum.Parse<Escalation>(ToEnumName(check.Escalation), true);
        var due = PreRun.DueEscalation(escalation, outcome, met);
        var next = PreRun.NextStep(due, outcome);

        return new PreRunStatus(
            check.Id,
            check.SentAt,
            check.SentBy,
            check.RespondedAt,
            check.ResponseMinutes ?? PreRun.MinutesTaken(check.SentAt, check.RespondedAt, now),
            met,
            PreRun.IsReady(outcome, met),
            check.Outcome,
            ToWireName(due),
            PreRun.Describe(due),
            next is null ? null : ToWireName(next.Value),
            check.ConfirmedBy,
            check.TruckNo,
            check.Driver,
            check.DriverContact,
            check.Correction,
            check.Remark);
    }

    /// <summary>"no-response" on the wire is NoResponse in the enum.</summary>
    private static string ToEnumName(string wire) => wire.Replace("-", "");

    private static string ToWireName(Escalation step) => step switch
    {
        Escalation.FollowUp => "follow-up",
        _ => step.ToString().ToLowerInvariant(),
    };
}
