using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <param name="Key">The job, so a row can open it.</param>
/// <param name="Why">Overdue · Unassigned · NoCarrier · NoTruck.</param>
/// <param name="DaysAway">Negative once the plan date has passed.</param>
public record RiskRow(string Key, string Why, int DaysAway, string Cat, string Date,
    string Customer, string Trucker, string Owner, string Status, string JobCode);

public record LoadRow(string OwnerId, string Owner, int Carrying, int Flagged, int OldestDaysWaiting,
    /// <summary>Who is covering for this person today, if anybody.</summary>
    string CoveredBy);

public record BlameRow(string Party, string Thai, int Cases, int Minutes, int Unmeasured);

/// <param name="Today">The date every count was taken against, so the screen can say so.</param>
public record MonitorBoard(
    IReadOnlyList<RiskRow> Risks,
    IReadOnlyList<LoadRow> Loads,
    IReadOnlyList<BlameRow> Blames,
    string Today,
    int Live);

/// <summary>
/// The supervisor's three questions, answered from the whole register.
///
/// Server-side rather than in the browser because the workspace pages: a screen
/// that counted what the browser happened to be holding would report one page
/// of a team's work as the team's work.
///
/// The judgements all live in <see cref="MonitorRules"/>; this reads the data
/// and hands it over. Nothing is decided here.
/// </summary>
public class MonitorService(ScmosDbContext db, JobRegisterCache register)
{
    /// <summary>
    /// One month back, for the delay summary.
    ///
    /// A supervisor asking where the time went means recently. Everything ever
    /// recorded would bury this month under last year's bad quarter.
    /// </summary>
    private const int DelayDays = 30;

    public async Task<MonitorBoard> ReadAsync(CancellationToken token)
    {
        var today = DateOnly.FromDateTime(Formats.Now.DateTime);

        var snapshot = await register.ReadAsync(token);
        var jobs = new List<WorkspaceTabs.JobView>(snapshot.Rows.Count);
        foreach (var row in snapshot.Rows)
        {
            var job = WorkspaceTabs.JobView.From(row.Raw);
            if (job.Key.Length > 0) jobs.Add(job);
        }

        var flags = jobs
            .Select(job => (Job: job, Flag: MonitorRules.Judge(job, today)))
            .Where(pair => pair.Flag is not null)
            .ToList();

        var byKey = jobs.ToDictionary(job => job.Key, StringComparer.Ordinal);
        var risks = MonitorRules
            .InReadingOrder(flags.Select(pair => pair.Flag!.Value))
            .Select(flag =>
            {
                var job = byKey[flag.Key];
                return new RiskRow(job.Key, flag.Why.ToString(), flag.DaysAway, job.Cat, job.Date,
                    job.Customer, job.Trucker, job.Owner, job.Status, job.JobCode);
            })
            .ToList();

        // Who is standing in for whom today, so the load board can say that a
        // quiet column belongs to somebody on leave rather than somebody idle.
        var cover = await db.JobDelegations.AsNoTracking().Where(grant => !grant.Revoked).ToListAsync(token);
        var staff = await db.Staff.AsNoTracking()
            .ToDictionaryAsync(person => person.Id, person => person.Name, token);
        var coveredBy = cover
            .Where(grant => DelegationService.IsLive(grant, today))
            .GroupBy(grant => grant.OwnerId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => string.Join(", ", group.Select(g => staff.GetValueOrDefault(g.DelegateId, g.DelegateId))),
                StringComparer.OrdinalIgnoreCase);

        var loads = MonitorRules.Loads(jobs, today)
            .Select(load => new LoadRow(load.OwnerId, load.Owner, load.Carrying, load.Flagged,
                load.OldestDaysWaiting, coveredBy.GetValueOrDefault(load.OwnerId, "")))
            .ToList();

        var since = DateTimeOffset.UtcNow.AddDays(-DelayDays);
        var delays = await db.DelayRecords.AsNoTracking()
            .Where(record => record.DetectedAt >= since)
            .Select(record => new { record.Responsible, record.ImpactMinutes })
            .ToListAsync(token);

        var blames = MonitorRules
            .Blames(delays.Select(record => (record.Responsible, record.ImpactMinutes)))
            .Select(blame => new BlameRow(blame.Party, ThaiParty(blame.Party),
                blame.Cases, blame.Minutes, blame.Unmeasured))
            .ToList();

        var live = jobs.Count(job => !JobRules.IsDone(job.Status) && !WorkspaceTabs.IsCancelled(job));
        return new MonitorBoard(risks, loads, blames, today.ToString("dd/MM/yyyy"), live);
    }

    /// <summary>
    /// The party in Thai, through the rules that already name them, so the
    /// monitor and the delay screen call the same people the same thing.
    /// </summary>
    private static string ThaiParty(string party) =>
        Enum.TryParse<ResponsibleParty>(party, ignoreCase: true, out var known)
            ? DelayReasons.Thai(known)
            : party;
}
