using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record Period(string Year, string Month, string Day)
{
    public static readonly Period All = new("", "", "");
    public bool IsAll => Year.Length == 0 && Month.Length == 0 && Day.Length == 0;
}

public record Counted(string Label, int Value);
/// <summary>A rate with the base it was measured over — 55% means little without the 630.</summary>
public record Measured(int Base, int Met, int Percent);
public record OwnerLoad(string Owner, string OwnerId, int Total, int Open, int Action, int Late);
public record CarrierLoad(string Carrier, int Total, int Measured, int OnTime, int Percent);

public record KpiReport(
    int Total,
    IReadOnlyList<Counted> ByCategory,
    IReadOnlyList<Counted> ByStatus,
    Measured OnTime,
    int ActionRequired,
    int FormatErrors,
    int GateInRisk,
    int Undated,
    IReadOnlyList<OwnerLoad> Team,
    IReadOnlyList<CarrierLoad> Carriers,
    IReadOnlyList<Counted> ByDay,
    Period Period,
    string ComputedAt);

/// <summary>
/// The operational KPIs, computed where the register lives.
///
/// This used to happen in the browser, in opsStats. It is here now because the
/// architecture puts business rules in .NET, and because a figure the management
/// dashboard reports should not depend on which build of the front end a viewer
/// happens to have loaded.
///
/// Every count comes from the same rules the workspace colours a row by, so a
/// job the grid calls "action required" is a job this counts as action required.
/// </summary>
public class KpiService(JobRegisterCache register)
{
    public async Task<KpiReport> BuildAsync(Period period, CancellationToken token)
    {
        // The same snapshot the measures engine reads, not a second query.
        //
        // This used to go to SQL directly while the engine read the shared
        // snapshot, and the snapshot lives five minutes. So for up to five
        // minutes after any edit or import, the two halves of the KPI screen
        // described different registers: the counts at the top current, the
        // measures underneath them from before. Same numbers, same instant, or
        // people are right not to trust either.
        //
        // The register is still judged in memory rather than in SQL. The rules
        // parse hand-typed dates and times that SQL cannot be trusted to read
        // the same way — that difference is the entire reason the data standard
        // exists — so correctness wins over pushing the work down.
        var snapshot = await register.ReadAsync(token);

        var jobs = new List<JobRecord>(snapshot.Rows.Count);
        foreach (var row in snapshot.Rows)
        {
            var job = row.Record;
            if (job is not null && Matches(job, period)) jobs.Add(job);
        }

        var measurable = jobs.Where(JobRules.IsMeasurable).ToList();
        var onTime = measurable.Count(JobRules.IsOnTime);

        var byStatus = jobs
            .GroupBy(job => Formats.Clean(job.Status) is { Length: > 0 } s ? s : "(ไม่ระบุ)")
            .Select(group => new Counted(group.Key, group.Count()))
            .OrderByDescending(entry => entry.Value)
            .ToList();

        var byCategory = jobs
            .GroupBy(job => job.Cat.Length > 0 ? job.Cat.ToUpperInvariant() : "(ไม่ระบุ)")
            .Select(group => new Counted(group.Key, group.Count()))
            .OrderByDescending(entry => entry.Value)
            .ToList();

        var team = jobs
            .Where(job => Formats.Clean(job.Op).Length > 0)
            .GroupBy(job => job.Op.Trim())
            .Select(group => new OwnerLoad(
                group.Key,
                group.Select(job => job.OpId).FirstOrDefault(id => id.Length > 0) ?? "",
                group.Count(),
                group.Count(job => !JobRules.IsDone(job.Status)),
                group.Count(JobRules.NeedsAction),
                group.Count(job => JobRules.IsDelayed(job.Status))))
            .OrderByDescending(entry => entry.Total)
            .ToList();

        var carriers = jobs
            .Where(job => Formats.Clean(job.Trucker).Length > 0)
            .GroupBy(job => job.Trucker.Trim().ToUpperInvariant())
            .Select(group =>
            {
                var measured = group.Count(JobRules.IsMeasurable);
                var met = group.Count(JobRules.IsOnTime);
                return new CarrierLoad(group.Key, group.Count(), measured, met, Percent(met, measured));
            })
            .OrderByDescending(entry => entry.Total)
            .ToList();

        var byDay = jobs
            .Where(job => Formats.DateNumber(job.Date) > 0)
            .GroupBy(job => job.Date.Trim())
            .Select(group => new Counted(group.Key, group.Count()))
            .OrderBy(entry => Formats.DateNumber(entry.Label))
            .ToList();

        return new KpiReport(
            jobs.Count,
            byCategory,
            byStatus,
            new Measured(Base: measurable.Count, Met: onTime, Percent: Percent(onTime, measurable.Count)),
            jobs.Count(JobRules.NeedsAction),
            jobs.Count(job => !JobRules.IsKpiReady(job)),
            jobs.Count(JobRules.GateInRisk),
            jobs.Count(job => Formats.DateNumber(job.Date) == 0),
            team,
            carriers,
            byDay,
            period,
            DateTimeOffset.UtcNow.ToString("O"));
    }

    /// <summary>
    /// The period filter, applied on the plan date the job carries. A job with
    /// no usable date is only in scope when nothing is being filtered — it
    /// belongs to no month, and quietly counting it in July would be a lie.
    /// </summary>
    private static bool Matches(JobRecord job, Period period) =>
        period.IsAll || JobRules.InPeriod(job.Date, period.Year, period.Month, period.Day);

    private static int Percent(int part, int whole) =>
        whole == 0 ? 0 : (int)Math.Round(part * 100.0 / whole, MidpointRounding.AwayFromZero);
}
