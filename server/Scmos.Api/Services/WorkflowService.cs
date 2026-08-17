using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record StageView(string Id, string English, string Thai, int Position, string? Gate, string? GateThai);

public record SupplierAttempt(
    long Id, int Rank, string Carrier, int? QuotedPrice, string Outcome, string Reason,
    DateTimeOffset RequestedAt, DateTimeOffset? RespondedAt, int? ResponseMinutes);

public record WorkflowEventView(
    long Id, string Kind, string From, string To, string Hold, string Note, string By, DateTimeOffset At);

public record JobWorkflow(
    string JobKey,
    string Reference,
    string Stage,
    int Position,
    string Hold,
    bool IsHeld,
    string? PendingGate,
    string? PendingGateThai,
    IReadOnlyList<SupplierAttempt> Suppliers,
    /// <summary>The approved order to ask carriers in, and who is next.</summary>
    IReadOnlyList<CarrierPriority> Priority,
    CarrierPriority? NextToAsk,
    IReadOnlyList<WorkflowEventView> History);

public record WorkflowOutcome(bool Ok, string Message, JobWorkflow? State);

/// <summary>
/// The process, enforced.
///
/// A job's position is derived from its events, never stored as a column that
/// can be set to anything: the last advance says where it is, the last hold says
/// whether it is parked. That makes the history the truth rather than a log
/// written alongside one.
///
/// Where the flow has no events yet — every one of the 2,102 jobs keyed before
/// this existed — the plan's own status decides the starting position, so the
/// workflow begins from where the work really got to.
/// </summary>
public class WorkflowService(ScmosDbContext db)
{
    /// <summary>
    /// How many measured runs a carrier needs before their on-time rate is
    /// allowed to rank them. Below this the percentage is noise.
    /// </summary>
    private const int MinimumSample = 5;

    public static IReadOnlyList<StageView> Definition() =>
        Workflow.Stages.Select(info => new StageView(
            info.Stage.ToString(), info.English, info.Thai, Workflow.Position(info.Stage),
            info.Gate?.Question, info.Gate?.Thai)).ToList();

    /// <summary>
    /// The owner id on a job, for the permission check. Null when there is no
    /// such job — which the caller must tell apart from "nobody owns it".
    /// </summary>
    public async Task<string?> OwnerOfAsync(string jobKey, CancellationToken token) =>
        await db.OperationJobs.AsNoTracking()
            .Where(job => job.Key == jobKey)
            .Select(job => job.OwnerId)
            .FirstOrDefaultAsync(token);

    public async Task<JobWorkflow?> ReadAsync(string jobKey, CancellationToken token)
    {
        var job = await db.OperationJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Key == jobKey, token);
        if (job is null) return null;

        var events = await db.WorkflowEvents.AsNoTracking()
            .Where(e => e.JobKey == jobKey).OrderBy(e => e.Id).ToListAsync(token);

        var suppliers = await db.SupplierRequests.AsNoTracking()
            .Where(s => s.JobKey == jobKey).OrderBy(s => s.Rank).ThenBy(s => s.Id).ToListAsync(token);

        var record = JobRecord.From(job.Data);
        var priority = await PriorityForAsync(job.Customer, job.Cat, token);
        return Compose(jobKey, record?.Reference ?? job.JobCode, record?.Status ?? job.Status,
            events, suppliers, priority);
    }

    /// <summary>
    /// The order to ask carriers in for a customer's work.
    ///
    /// Ranked on what the register can actually prove: who has run this
    /// customer's jobs, and how often they arrived on time. A carrier who has
    /// done fifty of these and keeps to the plan is asked before one who has
    /// done two.
    ///
    /// This is not yet the price order. The rate book — 2,270 quoted lanes —
    /// still lives as a file the web app serves, so the backend cannot see it.
    /// Once the Rate domain is in Azure SQL as the architecture has it, the
    /// cheapest confirmed-capable carrier becomes the first call and this
    /// becomes the tie-breaker behind it.
    /// </summary>
    public async Task<IReadOnlyList<CarrierPriority>> PriorityForAsync(
        string customer, string category, CancellationToken token)
    {
        if (customer.Trim().Length == 0) return [];

        var rows = await db.OperationJobs.AsNoTracking()
            .Where(job => job.Customer == customer && job.Cat == category && job.Trucker != "")
            .Select(job => new { job.Trucker, job.Data })
            .ToListAsync(token);

        if (rows.Count == 0) return [];

        var scored = rows
            .GroupBy(row => row.Trucker.Trim().ToUpperInvariant())
            .Where(group => group.Key.Length > 0)
            .Select(group =>
            {
                var records = group.Select(row => JobRecord.From(row.Data)).Where(r => r is not null).ToList();
                var measurable = records.Count(r => JobRules.IsMeasurable(r!));
                var onTime = records.Count(r => JobRules.IsOnTime(r!));
                var rated = measurable >= MinimumSample;
                var rate = rated ? (double)onTime / measurable : -1;
                return (Carrier: group.Key, Jobs: group.Count(), Measurable: measurable, OnTime: onTime, Rated: rated, Rate: rate);
            })
            // Rated carriers first, best on-time first. A carrier without enough
            // measured runs is not ranked by percentage at all: one job arriving
            // on time is not a 100% record, and putting that at the top of the
            // call list would send work to whoever happens to have the smallest
            // history. Those sort behind, on how much of this customer's work
            // they have actually carried.
            .OrderByDescending(entry => entry.Rated)
            .ThenByDescending(entry => entry.Rate)
            .ThenByDescending(entry => entry.Jobs)
            .ToList();

        return scored.Select((entry, index) => new CarrierPriority(
            index + 1,
            entry.Carrier,
            null,
            entry.Rated
                ? $"ตรงเวลา {Math.Round(entry.Rate * 100)}% ({entry.OnTime}/{entry.Measurable}) · เคยวิ่งให้ลูกค้านี้ {entry.Jobs} งาน"
                : $"เคยวิ่งให้ลูกค้านี้ {entry.Jobs} งาน · วัดตรงเวลาได้ {entry.Measurable} งาน ยังน้อยเกินจัดอันดับ")).ToList();
    }

    /// <summary>
    /// Answers the gate on the job's current stage, or moves it on when the
    /// stage has no gate.
    ///
    /// A failed gate does one of two things depending on the stage. Most park
    /// the job with a named reason. The capacity gate does not: a carrier saying
    /// no is the escalation loop, so the job goes back to supplier selection and
    /// stays workable.
    /// </summary>
    public async Task<WorkflowOutcome> AdvanceAsync(string jobKey, bool? answer, string note, string by, CancellationToken token)
    {
        var state = await ReadAsync(jobKey, token);
        if (state is null) return new WorkflowOutcome(false, "ไม่พบงานนี้", null);
        if (state.IsHeld) return new WorkflowOutcome(false, "งานถูกพักอยู่ — ต้องปลดล็อกก่อน", state);

        var stage = Enum.Parse<Stage>(state.Stage);
        var info = Workflow.Info(stage);

        if (info.Gate is not null)
        {
            if (answer is null)
                return new WorkflowOutcome(false, $"ต้องตอบก่อน: {info.Gate.Thai}", state);

            var passed = answer.Value == Workflow.PassMeansYes(stage);
            if (!passed)
            {
                if (info.Gate.OnFail == HoldReason.None)
                {
                    // The escalation loop: back a step, not parked.
                    await Record(jobKey, "advance", stage, info.Gate.ReturnsTo, "", note, by, token);
                    await db.SaveChangesAsync(token);
                    return new WorkflowOutcome(true, "ผู้ขนส่งไม่รับงาน — กลับไปเลือกเจ้าถัดไป",
                        await ReadAsync(jobKey, token));
                }

                await Record(jobKey, "hold", stage, info.Gate.ReturnsTo, info.Gate.OnFail.ToString(), note, by, token);
                await db.SaveChangesAsync(token);
                return new WorkflowOutcome(true, $"พักงานไว้: {Describe(info.Gate.OnFail)}",
                    await ReadAsync(jobKey, token));
            }
        }

        var next = Workflow.Next(stage);
        if (next is null) return new WorkflowOutcome(false, "งานปิดแล้ว", state);

        await Record(jobKey, "advance", stage, next.Value, "", note, by, token);
        await SyncStatus(jobKey, next.Value, token);
        await db.SaveChangesAsync(token);

        return new WorkflowOutcome(true, $"ไปยังขั้นตอน: {Workflow.Info(next.Value).Thai}",
            await ReadAsync(jobKey, token));
    }

    /// <summary>Parks a job by hand, for a reason the flow does not have a gate for.</summary>
    public async Task<WorkflowOutcome> HoldAsync(string jobKey, string reason, string note, string by, CancellationToken token)
    {
        var state = await ReadAsync(jobKey, token);
        if (state is null) return new WorkflowOutcome(false, "ไม่พบงานนี้", null);
        if (state.IsHeld) return new WorkflowOutcome(false, "งานนี้ถูกพักอยู่แล้ว", state);
        if (!Enum.TryParse<HoldReason>(reason, true, out var parsed) || parsed == HoldReason.None)
            return new WorkflowOutcome(false, "ต้องระบุเหตุผลที่พักงาน", state);

        var stage = Enum.Parse<Stage>(state.Stage);
        await Record(jobKey, "hold", stage, stage, parsed.ToString(), note, by, token);
        await db.SaveChangesAsync(token);
        return new WorkflowOutcome(true, $"พักงานไว้: {Describe(parsed)}", await ReadAsync(jobKey, token));
    }

    /// <summary>Puts a parked job back into the flow at the stage its hold returns to.</summary>
    public async Task<WorkflowOutcome> ReleaseAsync(string jobKey, string note, string by, CancellationToken token)
    {
        var state = await ReadAsync(jobKey, token);
        if (state is null) return new WorkflowOutcome(false, "ไม่พบงานนี้", null);
        if (!state.IsHeld) return new WorkflowOutcome(false, "งานนี้ไม่ได้ถูกพักไว้", state);

        var stage = Enum.Parse<Stage>(state.Stage);
        await Record(jobKey, "release", stage, stage, "", note, by, token);
        await db.SaveChangesAsync(token);
        return new WorkflowOutcome(true, "ปลดล็อกงานแล้ว", await ReadAsync(jobKey, token));
    }

    /// <summary>
    /// Asks a carrier, if the process allows it now.
    ///
    /// Every refusal here comes from <see cref="CarrierAssignment"/> and carries
    /// the reason with it. The rank is the position in the escalation and is
    /// worked out here rather than accepted from the caller — it is a fact about
    /// what has happened, not an input.
    /// </summary>
    public async Task<WorkflowOutcome> RequestSupplierAsync(
        string jobKey, string carrier, int? quotedPrice, string? skipReason, string by, CancellationToken token)
    {
        var state = await ReadAsync(jobKey, token);
        if (state is null) return new WorkflowOutcome(false, "ไม่พบงานนี้", null);

        var name = carrier.Trim();
        var attempts = Attempts(state);
        var breach = CarrierAssignment.CanRequest(name, attempts, state.Priority, skipReason);
        if (breach is not null) return new WorkflowOutcome(false, breach.Message, state);

        var rank = state.Suppliers.Count + 1;
        db.SupplierRequests.Add(new SupplierRequest
        {
            JobKey = jobKey, Rank = rank, Carrier = name, QuotedPrice = quotedPrice,
            Outcome = CarrierAssignment.Pending, RequestedBy = by, RequestedAt = DateTimeOffset.UtcNow,
        });

        var stage = Enum.Parse<Stage>(state.Stage);
        // Asking a carrier is what moves a job to "capacity requested"; doing it
        // from the selection stage advances the flow so the two cannot disagree.
        var moveTo = stage == Stage.SupplierSelection ? Stage.CapacityRequested : stage;
        var note = $"#{rank} {name}"
            + (quotedPrice is not null ? $" · {quotedPrice:N0} บาท" : "")
            + (string.IsNullOrWhiteSpace(skipReason) ? "" : $" · ข้ามลำดับ: {skipReason.Trim()}");

        await Record(jobKey, "supplier-request", stage, moveTo, "", note, by, token);
        await db.SaveChangesAsync(token);
        return new WorkflowOutcome(true, $"ขอรถจาก {name} แล้ว (ลำดับที่ {rank})", await ReadAsync(jobKey, token));
    }

    /// <summary>
    /// Puts the confirmed carrier onto the job.
    ///
    /// This is the only way a carrier reaches the register now. The booking
    /// screen used to write it directly, which made it possible to have a job
    /// assigned to a company that had never been asked.
    /// </summary>
    public async Task<WorkflowOutcome> AssignCarrierAsync(string jobKey, string carrier, string by, CancellationToken token)
    {
        var state = await ReadAsync(jobKey, token);
        if (state is null) return new WorkflowOutcome(false, "ไม่พบงานนี้", null);

        var name = carrier.Trim();
        var breach = CarrierAssignment.CanAssign(name, Attempts(state));
        if (breach is not null) return new WorkflowOutcome(false, breach.Message, state);

        var job = await db.OperationJobs.FirstOrDefaultAsync(j => j.Key == jobKey, token);
        if (job is null) return new WorkflowOutcome(false, "ไม่พบงานนี้", state);

        var node = System.Text.Json.Nodes.JsonNode.Parse(job.Data)?.AsObject();
        if (node is null) return new WorkflowOutcome(false, "ข้อมูลงานเสียหาย", state);

        var previous = job.Trucker;
        node["trucker"] = name;
        job.Trucker = name;
        job.Data = node.ToJsonString();
        job.UpdatedBy = by;
        job.UpdatedAt = DateTimeOffset.UtcNow;

        var stage = Enum.Parse<Stage>(state.Stage);
        await Record(jobKey, "assign-carrier", stage, stage, "",
            $"{(previous.Length > 0 ? previous + " → " : "")}{name}", by, token);

        await db.SaveChangesAsync(token);
        return new WorkflowOutcome(true, $"มอบหมายงานให้ {name} แล้ว", await ReadAsync(jobKey, token));
    }

    private static List<Attempt> Attempts(JobWorkflow state) =>
        state.Suppliers.Select(s => new Attempt(s.Carrier, s.Outcome, s.Rank)).ToList();

    /// <summary>Records what the carrier said, and how long they took to say it.</summary>
    public async Task<WorkflowOutcome> RespondSupplierAsync(
        string jobKey, long requestId, string outcome, string reason, string by, CancellationToken token)
    {
        var request = await db.SupplierRequests
            .FirstOrDefaultAsync(s => s.Id == requestId && s.JobKey == jobKey, token);
        if (request is null) return new WorkflowOutcome(false, "ไม่พบคำขอนี้", await ReadAsync(jobKey, token));
        if (request.Outcome != "pending")
            return new WorkflowOutcome(false, "คำขอนี้บันทึกผลไปแล้ว", await ReadAsync(jobKey, token));

        var allowed = new[] { "confirmed", "rejected", "cancelled", "no-response" };
        var value = outcome.Trim().ToLowerInvariant();
        if (!allowed.Contains(value))
            return new WorkflowOutcome(false, "ผลที่บันทึกได้: confirmed, rejected, cancelled, no-response",
                await ReadAsync(jobKey, token));

        request.Outcome = value;
        request.Reason = reason.Trim();
        request.RespondedAt = DateTimeOffset.UtcNow;

        var state = await ReadAsync(jobKey, token);
        var stage = state is null ? Stage.CapacityRequested : Enum.Parse<Stage>(state.Stage);
        var minutes = request.ResponseMinutes;

        await Record(jobKey, "supplier-response", stage, stage, "",
            $"#{request.Rank} {request.Carrier} → {value}" +
            (request.Reason.Length > 0 ? $" · {request.Reason}" : "") +
            (minutes is not null ? $" · {minutes} นาที" : ""), by, token);

        await db.SaveChangesAsync(token);
        return new WorkflowOutcome(true, $"บันทึกผลจาก {request.Carrier} แล้ว", await ReadAsync(jobKey, token));
    }

    /* --------------------------------------------------------------- inside */

    private async Task Record(string jobKey, string kind, Stage from, Stage to, string hold,
        string note, string by, CancellationToken token)
    {
        db.WorkflowEvents.Add(new WorkflowEvent
        {
            JobKey = jobKey,
            Kind = kind,
            FromStage = from.ToString(),
            ToStage = to.ToString(),
            Hold = hold,
            Note = note.Trim().Length > 500 ? note.Trim()[..500] : note.Trim(),
            By = by,
            At = DateTimeOffset.UtcNow,
        });
        await Task.CompletedTask;
    }

    /// <summary>
    /// Keeps the plan's status in step with the workflow. Not every stage has a
    /// status — the document checks happen between "truck confirmed" and
    /// "dispatched" and the ladder has no word for them — and those leave it be.
    /// </summary>
    private async Task SyncStatus(string jobKey, Stage stage, CancellationToken token)
    {
        var job = await db.OperationJobs.FirstOrDefaultAsync(j => j.Key == jobKey, token);
        if (job is null) return;

        var wanted = Workflow.StatusFor(stage, job.Cat);
        if (wanted is null || job.Status == wanted) return;

        var record = JobRecord.From(job.Data);
        if (record is null) return;

        var node = System.Text.Json.Nodes.JsonNode.Parse(job.Data)?.AsObject();
        if (node is null) return;
        node["status"] = wanted;

        job.Status = wanted;
        job.Data = node.ToJsonString();
        job.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static JobWorkflow Compose(string jobKey, string reference, string status,
        List<WorkflowEvent> events, List<SupplierRequest> suppliers,
        IReadOnlyList<CarrierPriority> priority)
    {
        // No events means the job predates the workflow: its status says where
        // the work actually got to, and that is where the flow picks it up.
        var stage = events.Count == 0
            ? Workflow.FromStatus(status)
            : Enum.Parse<Stage>(events[^1].ToStage);

        var hold = "";
        foreach (var entry in events)
        {
            if (entry.Kind == "hold") hold = entry.Hold;
            else if (entry.Kind == "release") hold = "";
        }

        var info = Workflow.Info(stage);
        var attempts = suppliers.Select(s => new Attempt(s.Carrier, s.Outcome, s.Rank)).ToList();

        return new JobWorkflow(
            jobKey,
            reference,
            stage.ToString(),
            Workflow.Position(stage),
            hold,
            hold.Length > 0,
            hold.Length > 0 ? null : info.Gate?.Question,
            hold.Length > 0 ? null : info.Gate?.Thai,
            suppliers.Select(s => new SupplierAttempt(
                s.Id, s.Rank, s.Carrier, s.QuotedPrice, s.Outcome, s.Reason,
                s.RequestedAt, s.RespondedAt, s.ResponseMinutes)).ToList(),
            priority,
            CarrierAssignment.NextInOrder(attempts, priority),
            events.Select(e => new WorkflowEventView(
                e.Id, e.Kind, e.FromStage, e.ToStage, e.Hold, e.Note, e.By, e.At)).ToList());
    }

    private static string Describe(HoldReason reason) => reason switch
    {
        HoldReason.CsCorrection => "ตีกลับ CS ให้แก้ข้อมูล",
        HoldReason.CsClarification => "รอเคลียร์กับ CS",
        HoldReason.BlMismatch => "B/L ไม่ตรงกับการจอง — แจ้ง CS แล้ว",
        HoldReason.ImageUnclear => "ภาพเอกสารไม่ชัด — ขอใหม่",
        HoldReason.Incident => "มีเหตุผิดปกติ — เข้ากระบวนการ CAR/PAR",
        _ => "ไม่ระบุ",
    };
}
