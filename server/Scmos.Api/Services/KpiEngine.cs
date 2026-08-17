using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// One measured figure.
///
/// <see cref="Available"/> is the field that matters. A rate with a base of zero
/// is not a good score, it is no score, and every consumer of this — the screen,
/// the Excel report, the AI agent that will be asked "what is Lotus's KPI this
/// month" — has to be able to tell the difference. <see cref="Note"/> says what
/// is missing when it cannot be measured.
/// </summary>
public record Measure(
    string Id,
    string English,
    string Thai,
    string Kind,
    bool Available,
    /// <summary>Percent for a rate, a count for a count. Null when unavailable.</summary>
    double? Value,
    /// <summary>How many records the figure was measured over.</summary>
    int Base,
    string Unit,
    string Note,
    IReadOnlyList<Counted> Breakdown,
    /// <summary>What the team agreed to hit, when somebody has agreed one.</summary>
    double? Target = null,
    /// <summary>Whether the figure meets it. Null when either side is unknown.</summary>
    bool? MeetsTarget = null,
    /// <summary>
    /// The same measure over the preceding months, oldest first. A number with
    /// no direction is a number nobody can act on: 55% is a crisis if it was 80%
    /// last month and a recovery if it was 40%.
    /// </summary>
    IReadOnlyList<TrendPoint>? Trend = null);

/// <param name="Period">"2026-07", as the month is written in a report.</param>
public record TrendPoint(string Period, double? Value, int Base);

public record SupplierScore(
    string Carrier,
    int Jobs,
    double? OnTime, int OnTimeBase,
    double? Confirmation, int ConfirmationBase,
    double? DelayFree, int DelayCount,
    int? Score);

public record KpiEngineReport(
    Period Period,
    int Jobs,
    IReadOnlyList<Measure> Measures,
    IReadOnlyList<SupplierScore> Suppliers,
    string ComputedAt);

/// <summary>
/// The KPI engine.
///
/// Shipment data lives in Azure SQL; this reads it, applies the same rules the
/// workspace colours a row by, and produces the eight measures the business
/// reports on. Nothing is estimated and nothing is filled in: where the records
/// that would answer a measure do not exist yet, the measure says so and names
/// what it needs.
/// </summary>
public class KpiEngine(ScmosDbContext db, IOptions<PreRunOptions> preRun)
{
    private readonly int _sla = preRun.Value.SlaMinutes > 0 ? preRun.Value.SlaMinutes : PreRun.DefaultSlaMinutes;

    public async Task<KpiEngineReport> BuildAsync(Period period, CancellationToken token)
    {
        var rows = await db.OperationJobs.AsNoTracking()
            .Select(job => new { job.Key, job.Trucker, job.Data })
            .ToListAsync(token);

        var jobs = new List<(string Key, string Carrier, JobRecord Record)>();
        foreach (var row in rows)
        {
            var record = JobRecord.From(row.Data);
            if (record is null || !InPeriod(record, period)) continue;
            jobs.Add((row.Key, row.Trucker.Trim().ToUpperInvariant(), record));
        }

        var keys = jobs.Select(job => job.Key).ToHashSet();

        var milestones = await db.ShipmentMilestones.AsNoTracking().ToListAsync(token);
        var requests = await db.SupplierRequests.AsNoTracking().ToListAsync(token);
        var preRuns = await db.PreRunChecks.AsNoTracking().ToListAsync(token);
        var delays = await db.DelayRecords.AsNoTracking().ToListAsync(token);
        var cases = await db.IncidentCases.AsNoTracking().ToListAsync(token);

        milestones = milestones.Where(m => keys.Contains(m.JobKey)).ToList();
        requests = requests.Where(r => keys.Contains(r.JobKey)).ToList();
        preRuns = preRuns.Where(p => keys.Contains(p.JobKey)).ToList();
        delays = delays.Where(d => keys.Contains(d.JobKey)).ToList();

        var measures = new List<Measure>
        {
            OnTimeDelivery(jobs),
            OnTimePickup(milestones),
            ConfirmationSla(requests, preRuns),
            Delay(delays, jobs),
            Accident(cases),
            CarPar(cases),
            Billing(),
            SupplierPerformance(jobs, requests, delays, out var scores),
        };

        return new KpiEngineReport(period, jobs.Count, measures, scores,
            DateTimeOffset.UtcNow.ToString("O"));
    }

    /// <summary>How many months of history the trend looks back over.</summary>
    private const int TrendMonths = 6;

    /// <summary>
    /// The report, with each measure carrying the preceding months alongside it.
    ///
    /// Built by running the same engine once per month rather than by a separate
    /// query, so a trend point and the headline figure can never be computed two
    /// different ways. It costs one pass per month over a register that is a few
    /// thousand rows; if that ever stops being cheap, cache it — do not fork the
    /// calculation.
    /// </summary>
    public async Task<KpiEngineReport> BuildWithTrendAsync(Period period, CancellationToken token)
    {
        var report = await BuildAsync(period, token);

        var months = await MonthsAsync(token);
        if (months.Count == 0) return report;

        // The months up to and including the one being reported on. Looking at
        // July while the trend runs to December would be a graph of the future.
        var upTo = period.Year.Length > 0 && period.Month.Length > 0
            ? $"{period.Year}-{period.Month}"
            : months[^1];
        var window = months.Where(month => string.CompareOrdinal(month, upTo) <= 0)
            .TakeLast(TrendMonths).ToList();
        if (window.Count < 2) return report;

        var byMonth = new List<(string Month, KpiEngineReport Report)>();
        foreach (var month in window)
        {
            var parts = month.Split('-');
            byMonth.Add((month, await BuildAsync(new Period(parts[0], parts[1], ""), token)));
        }

        var measures = report.Measures.Select(measure => measure with
        {
            Trend = byMonth
                .Select(entry =>
                {
                    var match = entry.Report.Measures.FirstOrDefault(m => m.Id == measure.Id);
                    return new TrendPoint(entry.Month, match?.Value, match?.Base ?? 0);
                })
                .ToList(),
        }).ToList();

        return report with { Measures = measures };
    }

    /// <summary>Every month the register has work in, oldest first.</summary>
    private async Task<List<string>> MonthsAsync(CancellationToken token)
    {
        var dates = await db.OperationJobs.AsNoTracking()
            .Select(job => job.WorkDate).ToListAsync(token);

        return dates
            .Select(date => Formats.PartsOf(date))
            .Where(parts => parts.Year.Length > 0 && parts.Month.Length > 0)
            .Select(parts => $"{parts.Year}-{parts.Month}")
            .Distinct()
            .OrderBy(month => month, StringComparer.Ordinal)
            .ToList();
    }

    /* ------------------------------------------------------------ measures */

    private static Measure OnTimeDelivery(List<(string Key, string Carrier, JobRecord Record)> jobs)
    {
        var measurable = jobs.Where(job => JobRules.IsMeasurable(job.Record)).ToList();
        var met = measurable.Count(job => JobRules.IsOnTime(job.Record));
        return Rate(MeasureId.OnTimeDelivery, met, measurable.Count,
            measurable.Count == 0
                ? "ไม่มีงานที่มีทั้งเวลาแผนและเวลาถึงที่อ่านได้"
                : $"วัดได้ {measurable.Count} จาก {jobs.Count} งาน — ที่เหลือขาดเวลาแผนหรือเวลาถึง");
    }

    private static Measure OnTimePickup(List<ShipmentMilestone> milestones)
    {
        // Only the pickup milestone, and only where both a plan and an actual
        // were recorded. The plan workbooks do not carry a pickup time, so this
        // measures what the operators entered, not what was imported.
        var pickups = milestones
            .Where(m => m.Stage == Stage.PickedUp.ToString() && m.ActualAt is not null)
            .Where(m => Formats.TimeMinutes(TimePart(m.PlannedAt)) is not null)
            .ToList();

        var met = pickups.Count(m =>
        {
            var planned = Formats.TimeMinutes(TimePart(m.PlannedAt));
            var actual = m.ActualAt!.Value.ToLocalTime();
            return planned is not null && actual.Hour * 60 + actual.Minute <= planned.Value;
        });

        return Rate(MeasureId.OnTimePickup, met, pickups.Count,
            pickups.Count == 0
                ? "ยังไม่มี milestone รับตู้ที่บันทึกทั้งเวลาแผนและเวลาจริง"
                : $"วัดจาก {pickups.Count} milestone ที่บันทึกไว้");
    }

    private Measure ConfirmationSla(List<SupplierRequest> requests, List<PreRunCheck> preRuns)
    {
        var answered = requests.Where(r => r.RespondedAt is not null).ToList();
        var answeredInTime = answered.Count(r =>
            (r.RespondedAt!.Value - r.RequestedAt).TotalMinutes <= _sla);

        var preRunAnswered = preRuns.Where(p => p.RespondedAt is not null).ToList();
        var preRunInTime = preRunAnswered.Count(p =>
            (p.RespondedAt!.Value - p.SentAt).TotalMinutes <= _sla);

        var total = answered.Count + preRunAnswered.Count;
        var met = answeredInTime + preRunInTime;

        var breakdown = new List<Counted>
        {
            new($"ขอกำลังรถ ตอบใน SLA", answeredInTime),
            new($"ขอกำลังรถ เกิน SLA", answered.Count - answeredInTime),
            new($"Pre-run ตอบใน SLA", preRunInTime),
            new($"Pre-run เกิน SLA", preRunAnswered.Count - preRunInTime),
            new("ขอกำลังรถ ยังไม่ตอบ", requests.Count(r => r.Outcome == "pending")),
            new("Pre-run ยังไม่ตอบ", preRuns.Count(p => p.Outcome == "pending")),
        };

        return Rate(MeasureId.ConfirmationSla, met, total,
            total == 0
                ? $"ยังไม่มีคำขอที่ได้รับคำตอบ — SLA ที่ตั้งไว้คือ {_sla} นาที"
                : $"SLA {_sla} นาที · วัดจากคำตอบ {total} ครั้ง",
            breakdown);
    }

    /// <summary>
    /// How much is running late.
    ///
    /// Reported zero for months while the workspace showed sixty-four delayed
    /// jobs, because it counted only <c>delay_records</c> — the categorised,
    /// attributed rows somebody is meant to write and nobody has. The register's
    /// own statuses are the weaker source and the one that actually has data, so
    /// they are used when the records are empty, and the note says which it is.
    /// </summary>
    private static Measure Delay(List<DelayRecord> delays,
        List<(string Key, string Carrier, JobRecord Record)> jobs)
    {
        if (delays.Count > 0)
        {
            var breakdown = delays
                .GroupBy(delay => delay.Category)
                .Select(group => new Counted(
                    DelayReasons.Thai(Enum.TryParse<DelayCategory>(group.Key, true, out var c) ? c : DelayCategory.Other),
                    group.Count()))
                .OrderByDescending(entry => entry.Value)
                .ToList();

            var impact = delays.Where(d => d.ImpactMinutes is not null).Sum(d => d.ImpactMinutes!.Value);
            var againstCarrier = delays.Count(d => d.AgainstCarrier);

            return Count(MeasureId.Delay, delays.Count, "รายการ",
                $"รวมกระทบ {impact:N0} นาที · เป็นความรับผิดชอบของผู้ขนส่ง {againstCarrier} รายการ",
                breakdown);
        }

        var held = jobs.Where(job => JobRules.WasDelayed(job.Record)).ToList();
        if (held.Count == 0)
        {
            return Count(MeasureId.Delay, 0, "รายการ",
                "ไม่มีงานที่พักไว้ในทะเบียน และยังไม่มีการบันทึกความล่าช้าแยกรายการ", []);
        }

        // Grouped by carrier rather than by category: without delay records
        // there is no category, and inventing one would be the same mistake in a
        // different place.
        var byCarrier = held
            .Where(job => job.Carrier.Length > 0)
            .GroupBy(job => job.Carrier)
            .Select(group => new Counted(group.Key, group.Count()))
            .OrderByDescending(entry => entry.Value)
            .Take(8)
            .ToList();

        return Count(MeasureId.Delay, held.Count, "รายการ",
            "นับจากสถานะในทะเบียน (ยังไม่มีการบันทึกสาเหตุแยกรายการ) — ตัวเลขนี้ต่ำกว่าความจริง " +
            "เพราะงานที่ล่าช้าแล้วส่งจบไปแล้วจะไม่เหลือร่องรอยในสถานะ",
            byCarrier);
    }

    private static Measure Accident(List<IncidentCase> cases)
    {
        var accidents = cases.Where(c => c.Category.Equals("accident", StringComparison.OrdinalIgnoreCase)).ToList();
        var breakdown = accidents
            .GroupBy(c => c.Stage)
            .Select(group => new Counted(group.Key, group.Count()))
            .ToList();

        return Count(MeasureId.Accident, accidents.Count, "เคส",
            cases.Count == 0
                ? "ยังไม่มีเคสในระบบ"
                : $"จากเคสทั้งหมด {cases.Count} เคส",
            breakdown);
    }

    private static Measure CarPar(List<IncidentCase> cases)
    {
        var open = cases.Where(c => c.Stage != "closed").ToList();
        var today = Formats.DateNumber(DateTimeOffset.Now.ToString("dd/MM/yyyy"));
        var overdue = open.Count(c =>
        {
            var due = Formats.DateNumber(c.DueDate);
            return due > 0 && due < today;
        });

        var breakdown = new List<Counted>
        {
            new("เปิดอยู่", open.Count),
            new("เกินกำหนด", overdue),
            new("ปิดแล้ว", cases.Count - open.Count),
            new("CAR", cases.Count(c => c.Kind == "CAR")),
            new("PAR", cases.Count(c => c.Kind == "PAR")),
        };

        return Count(MeasureId.CarPar, open.Count, "เคสเปิดอยู่",
            cases.Count == 0 ? "ยังไม่มีเคส CAR/PAR ในระบบ" : $"เกินกำหนด {overdue} เคส",
            breakdown);
    }

    /// <summary>
    /// Billing has no source yet.
    ///
    /// The rule exists and the team already measures itself against it —
    /// invoices within four calendar days of completion — but there is no
    /// invoice table, so there is nothing to count. Reporting this as 0% would
    /// say the suppliers never invoice on time, which is not what is known.
    /// </summary>
    private static Measure Billing() =>
        new(MeasureId.Billing.ToString(),
            KpiMeasures.Of(MeasureId.Billing).English,
            KpiMeasures.Of(MeasureId.Billing).Thai,
            "Rate", false, null, 0, "%",
            "ยังวัดไม่ได้ — ระบบยังไม่มีตารางใบแจ้งหนี้ผู้รับเหมา จึงไม่มีอะไรให้นับเทียบกับกำหนด 4 วัน",
            []);

    private Measure SupplierPerformance(
        List<(string Key, string Carrier, JobRecord Record)> jobs,
        List<SupplierRequest> requests,
        List<DelayRecord> delays,
        out IReadOnlyList<SupplierScore> scores)
    {
        var delayByJob = delays.Where(d => d.AgainstCarrier)
            .GroupBy(d => d.JobKey)
            .ToDictionary(group => group.Key, group => group.Count());

        // Whether anything can be said about delays at all, and from what.
        //
        // This used to read delay_records alone. Nothing writes to that table
        // yet, so every carrier came out delay-free at exactly 100% and each
        // collected a perfect fifth of their score for it — the least-known
        // carrier and the best one scored identically on the component meant to
        // separate them. The register does know about delays: a job sitting at a
        // held or delayed status is delayed, and there are sixty-four of them.
        var evidence = delayByJob.Count > 0 ? DelayEvidence.Records
            : jobs.Any(job => JobRules.WasDelayed(job.Record)) ? DelayEvidence.Status
                : DelayEvidence.None;

        var built = jobs
            .Where(job => job.Carrier.Length > 0)
            .GroupBy(job => job.Carrier)
            .Select(group =>
            {
                var measurable = group.Where(job => JobRules.IsMeasurable(job.Record)).ToList();
                var onTimeBase = measurable.Count;
                double? onTime = onTimeBase == 0 ? null : (double)measurable.Count(j => JobRules.IsOnTime(j.Record)) / onTimeBase;

                var answered = requests
                    .Where(r => r.Carrier.Trim().ToUpperInvariant() == group.Key && r.RespondedAt is not null)
                    .ToList();
                double? confirmation = answered.Count == 0
                    ? null
                    : (double)answered.Count(r => (r.RespondedAt!.Value - r.RequestedAt).TotalMinutes <= _sla) / answered.Count;

                var jobKeys = group.Select(job => job.Key).ToList();
                var delayed = evidence switch
                {
                    DelayEvidence.Records => jobKeys.Count(key => delayByJob.ContainsKey(key)),
                    DelayEvidence.Status => group.Count(job => JobRules.WasDelayed(job.Record)),
                    _ => 0,
                };
                // Null, not 100, when nothing can say. That distinction is the
                // whole point of the component.
                double? delayFree = evidence == DelayEvidence.None || jobKeys.Count == 0
                    ? null
                    : 1.0 - (double)delayed / jobKeys.Count;

                return new SupplierScore(
                    group.Key, group.Count(),
                    Percent(onTime), onTimeBase,
                    Percent(confirmation), answered.Count,
                    Percent(delayFree), delayed,
                    KpiMeasures.Score(onTime, onTimeBase, confirmation, answered.Count, delayFree, jobKeys.Count));
            })
            .OrderByDescending(entry => entry.Score ?? -1)
            .ThenByDescending(entry => entry.Jobs)
            .ToList();

        scores = built;

        var scored = built.Where(entry => entry.Score is not null).ToList();
        var average = scored.Count == 0 ? (double?)null : scored.Average(entry => entry.Score!.Value);

        return new Measure(
            MeasureId.SupplierPerformance.ToString(),
            KpiMeasures.Of(MeasureId.SupplierPerformance).English,
            KpiMeasures.Of(MeasureId.SupplierPerformance).Thai,
            "Rate",
            average is not null,
            average is null ? null : Math.Round(average.Value, 1),
            scored.Count,
            "คะแนน",
            scored.Count == 0
                ? "ยังไม่มีผู้ขนส่งที่มีข้อมูลพอให้คะแนน"
                : $"คะแนนเฉลี่ยจาก {scored.Count} ผู้ขนส่ง · ถ่วงน้ำหนัก ตรงเวลา {KpiMeasures.WeightOnTime:P0} · ตอบยืนยัน {KpiMeasures.WeightConfirmation:P0} · ไม่มีความล่าช้า {KpiMeasures.WeightDelayFree:P0}",
            built.Take(12).Select(entry => new Counted(entry.Carrier, entry.Score ?? 0)).ToList());
    }

    /* -------------------------------------------------------------- shared */

    private static Measure Rate(MeasureId id, int met, int measured, string note,
        IReadOnlyList<Counted>? breakdown = null)
    {
        var definition = KpiMeasures.Of(id);
        var value = measured == 0 ? (double?)null : Math.Round(met * 100.0 / measured, 1);
        return new Measure(
            id.ToString(), definition.English, definition.Thai, "Rate",
            measured > 0, value, measured, "%", note, breakdown ?? [],
            definition.Target, Meets(definition, value));
    }

    private static Measure Count(MeasureId id, int value, string unit, string note,
        IReadOnlyList<Counted>? breakdown = null)
    {
        var definition = KpiMeasures.Of(id);
        // A count of zero is a real answer — no accidents is the best possible
        // result — so a count is always available, unlike a rate with no base.
        return new Measure(
            id.ToString(), definition.English, definition.Thai, "Count",
            true, value, value, unit, note, breakdown ?? [],
            definition.Target, Meets(definition, value));
    }

    /// <summary>
    /// Whether a figure meets its target, or null when either side is unknown.
    /// A measure with no target is not failing one.
    /// </summary>
    private static bool? Meets(MeasureDefinition definition, double? value) =>
        definition.Target is not { } target || value is not { } actual
            ? null
            : definition.HigherIsBetter ? actual >= target : actual <= target;

    private static double? Percent(double? ratio) =>
        ratio is null ? null : Math.Round(ratio.Value * 100, 1);

    /// <summary>"01/07/2026 11:00" — the time half, for comparing against an actual.</summary>
    private static string TimePart(string planned)
    {
        var parts = planned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch { 0 => "", 1 => parts[0].Contains(':') ? parts[0] : "", _ => parts[^1] };
    }

    private static bool InPeriod(JobRecord job, Period period)
    {
        if (period.IsAll) return true;
        var (year, month, day) = Formats.PartsOf(job.Date);
        if (year.Length == 0) return false;
        if (period.Year.Length > 0 && period.Year != year) return false;
        if (period.Month.Length > 0 && period.Month != month) return false;
        if (period.Day.Length > 0 && period.Day != day) return false;
        return true;
    }
}
