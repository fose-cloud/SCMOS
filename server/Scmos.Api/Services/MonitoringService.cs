using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <param name="Source">
/// Where this stage's actual time came from: <c>recorded</c> when somebody
/// entered it on the monitor, <c>register</c> when the job's own row already
/// carried it, empty when nothing has happened yet.
///
/// Said out loud rather than blended in. A time the team keyed into My Job and
/// a time somebody stood on a loading bay and recorded are both facts, and they
/// are not the same fact — the second says a person was watching.
/// </param>
/// <param name="LateMinutes">
/// Minutes past plan, when both are known. Negative when it ran early, null
/// when the pair cannot be compared. From <see cref="JobRules.MinutesLate"/>,
/// which is the one reading of this in the system.
/// </param>
public record MilestoneView(
    string Stage, string English, string Thai, string PlannedAt, DateTimeOffset? ActualAt,
    string Status, string Carrier, string TruckNo, string Driver, string Remark,
    string DelayReason, string PhotoKey, string UpdatedBy, DateTimeOffset? UpdatedAt,
    string Source, int? LateMinutes, string ActualText);

public record DelayView(
    long Id, string Stage, string Category, string CategoryThai, string Detail,
    string Responsible, string ResponsibleThai, string ClassifiedBy, string ClassifierBasis,
    DateTimeOffset DetectedAt, int? ImpactMinutes, DateTimeOffset? NotifiedAt, string NotifiedTeam,
    string RecoveryAction, DateTimeOffset? ResolvedAt, bool AgainstCarrier);

public record ShipmentTrack(
    string JobKey, string Reference, string Customer,
    IReadOnlyList<MilestoneView> Milestones, IReadOnlyList<DelayView> Delays);

public record MonitorResult(bool Ok, string Message);

/// <summary>
/// Shipment monitoring and delay management.
///
/// The seven operational milestones are always present for a job, whether or not
/// anything has been recorded against them — a journey with no pickup row is a
/// journey where nobody has said what happened at pickup, and that is different
/// from a journey with no pickup. Missing rows are returned as pending rather
/// than left out, so the gap is visible.
/// </summary>
public class MonitoringService(ScmosDbContext db)
{
    /// <summary>The run itself, in order. The stages before dispatch are booking, not monitoring.</summary>
    public static readonly Stage[] Tracked =
    [
        Stage.Dispatched, Stage.PickedUp, Stage.Loading, Stage.LoadingComplete, Stage.InTransit,
        Stage.Delivered, Stage.ContainerReturned, Stage.Closed,
    ];

    /// <param name="JobKey">The job the time belongs to.</param>
    /// <param name="Stage">Dispatched · PickedUp · Loading · LoadingComplete · InTransit · …</param>
    public record StageTime(string JobKey, string Stage, DateTimeOffset? ActualAt, string Status);

    /// <summary>
    /// Every recorded time for one customer's jobs, in one answer.
    ///
    /// The customer truck reports print a month of containers with six movement
    /// times each. Asking per shipment would be one request per row — a hundred
    /// and more on a busy month, against a database that takes a minute to wake.
    /// Only the four fields the report draws, so the payload stays small.
    /// </summary>
    public async Task<IReadOnlyList<StageTime>> TimesForCustomerAsync(
        string customer, CancellationToken token)
    {
        var wanted = customer.Trim();
        if (wanted.Length == 0) return [];

        // Through the job table rather than a key list from the browser: the
        // caller naming its own keys would let any signed-in person read the
        // times of jobs they never see, and the URL would carry a hundred keys.
        var keys = await db.OperationJobs.AsNoTracking()
            .Where(job => EF.Functions.Like(job.Customer, wanted))
            .Select(job => job.Key)
            .ToListAsync(token);

        if (keys.Count == 0) return [];
        var owned = keys.ToHashSet(StringComparer.Ordinal);

        var rows = await db.ShipmentMilestones.AsNoTracking()
            .Where(row => owned.Contains(row.JobKey))
            .Select(row => new StageTime(row.JobKey, row.Stage, row.ActualAt, row.Status))
            .ToListAsync(token);

        return rows;
    }

    public async Task<ShipmentTrack?> ReadAsync(string jobKey, CancellationToken token)
    {
        var job = await db.OperationJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Key == jobKey, token);
        if (job is null) return null;

        var record = JobRecord.From(job.Data);
        var rows = await db.ShipmentMilestones.AsNoTracking()
            .Where(m => m.JobKey == jobKey).ToListAsync(token);
        var delays = await db.DelayRecords.AsNoTracking()
            .Where(d => d.JobKey == jobKey).OrderBy(d => d.DetectedAt).ToListAsync(token);

        var milestones = Tracked.Select(stage =>
        {
            var info = Workflow.Info(stage);
            var row = rows.FirstOrDefault(m => m.Stage == stage.ToString());

            /*
             * What the job's own row already says happened at this stage.
             *
             * Every stage read "ยังไม่บันทึก" on all but one job of two
             * thousand, because nothing has been recorded through this screen
             * yet — while the register beside it had the arrival written down
             * all along. A monitor that shows a blank track for a shipment its
             * own database knows arrived is a monitor nobody opens twice.
             *
             * Only where the register genuinely holds the fact, and always
             * labelled as coming from there rather than from somebody watching.
             */
            var known = row is null ? RegisterActual(stage, record) : "";
            var status = row?.Status ?? (known.Length > 0 ? "done" : "pending");
            var source = row is not null ? "recorded" : known.Length > 0 ? "register" : "";

            return new MilestoneView(
                stage.ToString(), info.English, info.Thai,
                row?.PlannedAt ?? PlannedFor(stage, record),
                row?.ActualAt,
                status,
                row?.Carrier ?? record?.Trucker ?? "",
                row?.TruckNo ?? record?.Licence ?? "",
                row?.Driver ?? record?.Driver ?? "",
                row?.Remark ?? "",
                row?.DelayReason ?? "",
                row?.PhotoKey ?? "",
                row?.UpdatedBy ?? "",
                row?.UpdatedAt,
                source,
                LateFor(stage, record),
                known);
        }).ToList();

        return new ShipmentTrack(jobKey, record?.Reference ?? job.JobCode, job.Customer, milestones,
            delays.Select(Describe).ToList());
    }

    /// <summary>
    /// Records what happened at a milestone.
    ///
    /// A delayed milestone must say why. The plan already proves that a delay
    /// box nobody is made to fill in stays empty: the July register has two
    /// delayed jobs and no reason on either.
    /// </summary>
    public async Task<MonitorResult> UpdateAsync(string jobKey, string stage, DateTimeOffset? actualAt,
        string status, string truckNo, string driver, string remark, string delayReason, string photoKey,
        string by, CancellationToken token)
    {
        if (!Enum.TryParse<Stage>(stage, true, out var parsed) || !Tracked.Contains(parsed))
            return new MonitorResult(false, "ขั้นตอนนี้ไม่ได้อยู่ในช่วงติดตามการขนส่ง");

        var wanted = status.Trim().ToLowerInvariant();
        if (wanted.Length == 0) wanted = "done";
        if (wanted is not ("pending" or "done" or "delayed" or "skipped"))
            return new MonitorResult(false, "สถานะที่บันทึกได้: pending, done, delayed, skipped");

        if (wanted == "delayed" && delayReason.Trim().Length == 0)
            return new MonitorResult(false, "ระบุสาเหตุความล่าช้าด้วย — บันทึกว่าล่าช้าโดยไม่มีเหตุผลไม่ได้");

        var job = await db.OperationJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Key == jobKey, token);
        if (job is null) return new MonitorResult(false, "ไม่พบงานนี้");
        var record = JobRecord.From(job.Data);

        var row = await db.ShipmentMilestones
            .FirstOrDefaultAsync(m => m.JobKey == jobKey && m.Stage == parsed.ToString(), token);

        if (row is null)
        {
            row = new ShipmentMilestone
            {
                JobKey = jobKey,
                Stage = parsed.ToString(),
                PlannedAt = PlannedFor(parsed, record),
                Carrier = record?.Trucker ?? "",
            };
            db.ShipmentMilestones.Add(row);
        }

        row.Status = wanted;
        row.ActualAt = wanted is "done" or "delayed" ? actualAt ?? DateTimeOffset.UtcNow : actualAt;
        if (truckNo.Trim().Length > 0) row.TruckNo = truckNo.Trim();
        if (driver.Trim().Length > 0) row.Driver = driver.Trim();
        if (remark.Trim().Length > 0) row.Remark = remark.Trim();
        if (delayReason.Trim().Length > 0) row.DelayReason = delayReason.Trim();
        if (photoKey.Trim().Length > 0) row.PhotoKey = photoKey.Trim();
        row.UpdatedBy = by;
        row.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(token);
        return new MonitorResult(true, $"บันทึก {Workflow.Info(parsed).Thai} แล้ว");
    }

    /// <summary>
    /// Records a delay, with the category suggested from what was written.
    ///
    /// The suggestion is applied when the caller does not name a category, and
    /// how it was arrived at travels with the row — a delay booked against a
    /// subcontractor has to be defensible in a supplier meeting.
    /// </summary>
    public async Task<MonitorResult> RecordDelayAsync(string jobKey, string stage, string? category,
        string detail, int? impactMinutes, string by, CancellationToken token)
    {
        var job = await db.OperationJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Key == jobKey, token);
        if (job is null) return new MonitorResult(false, "ไม่พบงานนี้");
        if (detail.Trim().Length == 0 && string.IsNullOrWhiteSpace(category))
            return new MonitorResult(false, "ต้องระบุสาเหตุหรือหมวดความล่าช้า");

        DelayCategory chosen;
        ResponsibleParty responsible;
        string classifiedBy, basis;

        if (!string.IsNullOrWhiteSpace(category) && Enum.TryParse<DelayCategory>(category, true, out var named))
        {
            chosen = named;
            responsible = DelayReasons.ResponsibleFor(named);
            classifiedBy = "human";
            basis = "ผู้ใช้เลือกหมวดเอง";
        }
        else
        {
            var suggestion = DelayReasons.Classify(detail);
            chosen = suggestion.Category;
            responsible = suggestion.Responsible;
            classifiedBy = "rule";
            basis = suggestion.Basis;
        }

        db.DelayRecords.Add(new DelayRecord
        {
            JobKey = jobKey,
            Stage = stage.Trim(),
            Category = chosen.ToString(),
            Detail = detail.Trim(),
            Responsible = responsible.ToString(),
            ClassifiedBy = classifiedBy,
            ClassifierBasis = basis,
            DetectedAt = DateTimeOffset.UtcNow,
            ImpactMinutes = impactMinutes,
            AgainstCarrier = DelayReasons.CountsAgainstCarrier(chosen),
            RecordedBy = by,
        });

        await db.SaveChangesAsync(token);
        return new MonitorResult(true,
            $"บันทึกความล่าช้า หมวด {DelayReasons.Thai(chosen)} · ผู้รับผิดชอบ {DelayReasons.Thai(responsible)} ({basis})");
    }

    /// <summary>Notifies the responsible team, and records the recovery action once there is one.</summary>
    public async Task<MonitorResult> UpdateDelayAsync(long id, string? notifiedTeam, string? recoveryAction,
        bool? resolved, CancellationToken token)
    {
        var delay = await db.DelayRecords.FirstOrDefaultAsync(d => d.Id == id, token);
        if (delay is null) return new MonitorResult(false, "ไม่พบรายการความล่าช้านี้");

        if (!string.IsNullOrWhiteSpace(notifiedTeam))
        {
            delay.NotifiedTeam = notifiedTeam.Trim();
            delay.NotifiedAt = DateTimeOffset.UtcNow;
        }
        if (!string.IsNullOrWhiteSpace(recoveryAction)) delay.RecoveryAction = recoveryAction.Trim();
        if (resolved == true && delay.ResolvedAt is null) delay.ResolvedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(token);
        return new MonitorResult(true, "อัปเดตรายการความล่าช้าแล้ว");
    }

    /// <summary>What the classifier would say, without writing anything. For the UI to preview.</summary>
    public static DelaySuggestion Suggest(string detail) => DelayReasons.Classify(detail);

    /* --------------------------------------------------------------- inside */

    /// <summary>
    /// The planned time for a milestone, taken from the plan where the plan has
    /// one. Only two of the seven are planned in the workbooks — loading and
    /// arrival — and inventing the rest would make a schedule the team never
    /// agreed to.
    /// </summary>
    private static string PlannedFor(Stage stage, JobRecord? job)
    {
        if (job is null) return "";
        return stage switch
        {
            Stage.Loading => Join(job.Date, job.PlanTime),
            // Delivered used to read the arrival columns here, which put an
            // actual time under a heading that says "plan" — the one place this
            // screen showed the register at all, and it showed it as the wrong
            // thing. The register carries no planned delivery time, so the
            // honest answer is that it does not say.
            Stage.ContainerReturned => Join(job.ClosingDate, job.ClosingTime),
            _ => "",
        };
    }

    /// <summary>
    /// What the register already records as having happened at this stage.
    ///
    /// Only loading. The arrival columns are headed ARRIVAL DATE / TIME LOANDING
    /// / ACTUAL TIME on the plans the team imports, and the plan columns beside
    /// them are PLAN LOADING TIME — the pair describes one event, the lorry
    /// reaching the loading point, and it is the pair every on-time figure in
    /// the system is measured from. Nothing in the register says when a delivery
    /// or a container return actually happened, so nothing is claimed for them.
    /// </summary>
    private static string RegisterActual(Stage stage, JobRecord? job) =>
        job is not null && stage == Stage.Loading ? Join(job.ArrDate, job.ArrTime) : "";

    /// <summary>
    /// How late the loading arrival ran, through the reading the carrier
    /// scorecard and the supervisor monitor both use.
    /// </summary>
    private static int? LateFor(Stage stage, JobRecord? job)
    {
        if (job is null || stage != Stage.Loading) return null;
        var late = JobRules.MinutesLate(job);
        return late is null ? null : (int)Math.Round(late.Value);
    }

    private static string Join(string date, string time) =>
        string.Join(" ", new[] { Formats.Clean(date), Formats.Clean(time) }.Where(part => part.Length > 0));

    private static DelayView Describe(DelayRecord delay)
    {
        var category = Enum.TryParse<DelayCategory>(delay.Category, true, out var c) ? c : DelayCategory.Other;
        var party = Enum.TryParse<ResponsibleParty>(delay.Responsible, true, out var p) ? p : ResponsibleParty.None;
        return new DelayView(
            delay.Id, delay.Stage, delay.Category, DelayReasons.Thai(category), delay.Detail,
            delay.Responsible, DelayReasons.Thai(party), delay.ClassifiedBy, delay.ClassifierBasis,
            delay.DetectedAt, delay.ImpactMinutes, delay.NotifiedAt, delay.NotifiedTeam,
            delay.RecoveryAction, delay.ResolvedAt, delay.AgainstCarrier);
    }
}
