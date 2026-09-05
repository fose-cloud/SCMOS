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

/// <param name="Problems">Machine names, worst first, so the screen can colour them.</param>
/// <param name="ProblemsThai">The same list in the words the team uses.</param>
/// <param name="MinutesLate">Zero when the arrival was not late, and zero when it could not be measured.</param>
/// <param name="Measurable">Whether the plan and the arrival were both recorded well enough to compare.</param>
/// <param name="Note">What an operator wrote about it, unedited.</param>
/// <param name="NoteFrom">Which column or table they wrote it in.</param>
/// <param name="Planned">Plan date and time as the register holds them, unparsed.</param>
/// <param name="Arrived">Arrival date and time, likewise.</param>
public record ProblemRow(
    string Key, IReadOnlyList<string> Problems, IReadOnlyList<string> ProblemsThai,
    int MinutesLate, bool Measurable, string Note, string NoteFrom,
    string Date, string Customer, string Trucker, string Owner, string Status, string JobCode,
    // The two readings the lateness was worked out from, sent so the screen can
    // show its own evidence. Most late rows carry no note at all — the operator
    // never typed one — and a column of "ไม่มีข้อความ" teaches a supervisor
    // nothing, where "plan 09:00, arrived 09:30 two days later" is the whole
    // finding and shows a mis-keyed plan time for what it is.
    string Planned, string Arrived);

/// <param name="Unmeasurable">
/// Live jobs whose lateness cannot be worked out at all.
///
/// On the board beside the rest rather than left off it. Without this number a
/// quiet morning and a morning nobody filled in look exactly alike.
/// </param>
public record ProblemTally(int Live, int WithProblem, int Unmeasurable, int ArrivedLate, int LateMinutes);

/// <param name="Today">The date every count was taken against, so the screen can say so.</param>
public record MonitorBoard(
    IReadOnlyList<RiskRow> Risks,
    IReadOnlyList<LoadRow> Loads,
    IReadOnlyList<BlameRow> Blames,
    string Today,
    int Live,
    /// <summary>What has gone wrong with the work already running. Appended, so
    /// a browser holding the old shape still reads every field it knew.</summary>
    IReadOnlyList<ProblemRow> Problems,
    ProblemTally Tally);

/// <summary>
/// The supervisor's three questions, answered from the whole register.
///
/// Server-side rather than in the browser because the workspace pages: a screen
/// that counted what the browser happened to be holding would report one page
/// of a team's work as the team's work.
///
/// Four now: a supervisor also asked what has gone wrong with the work already
/// running, which is <see cref="ProblemRules"/> and a different set of jobs —
/// the risk list is about pieces missing before a shipment goes, and drops a
/// job the moment its lorry turns up.
///
/// The judgements all live in <see cref="MonitorRules"/> and
/// <see cref="ProblemRules"/>; this reads the data and hands it over. Nothing is
/// decided here.
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

        // One read for both questions. The summary wants the last month; the
        // problem list wants everything still open, however long ago it started
        // — an unresolved delay does not stop being unresolved at thirty days,
        // and a second query for the overlap would read most rows twice.
        var since = DateTimeOffset.UtcNow.AddDays(-DelayDays);
        var delays = await db.DelayRecords.AsNoTracking()
            .Where(record => record.DetectedAt >= since || record.ResolvedAt == null)
            .Select(record => new
            {
                record.JobKey, record.Responsible, record.ImpactMinutes,
                record.Detail, record.DetectedAt, record.ResolvedAt,
            })
            .ToListAsync(token);

        var blames = MonitorRules
            .Blames(delays
                .Where(record => record.DetectedAt >= since)
                .Select(record => (record.Responsible, record.ImpactMinutes)))
            .Select(blame => new BlameRow(blame.Party, ThaiParty(blame.Party),
                blame.Cases, blame.Minutes, blame.Unmeasured))
            .ToList();

        var live = jobs.Count(job => !JobRules.IsDone(job.Status) && !WorkspaceTabs.IsCancelled(job));

        var (problems, tally) = await ProblemsAsync(snapshot, delays
            .Where(record => record.ResolvedAt is null)
            .Select(record => (record.JobKey, record.Detail, record.DetectedAt)), token);

        return new MonitorBoard(risks, loads, blames, today.ToString("dd/MM/yyyy"), live,
            problems, tally);
    }

    /// <summary>
    /// What has gone wrong with the work in flight.
    ///
    /// The register knows most of it — an incident somebody typed, a reason in
    /// the delay column, an arrival measured against its plan. The two things it
    /// does not know are here: a categorised delay nobody closed, and a milestone
    /// an operator marked late. Both have only ever been visible inside the one
    /// job they belong to.
    /// </summary>
    private async Task<(IReadOnlyList<ProblemRow> Rows, ProblemTally Tally)> ProblemsAsync(
        JobRegisterSnapshot snapshot,
        IEnumerable<(string JobKey, string Detail, DateTimeOffset DetectedAt)> openDelays,
        CancellationToken token)
    {
        var open = openDelays
            .GroupBy(record => record.JobKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                // The most recent, because it is the one still being worked on.
                group => (Count: group.Count(),
                    Note: group.OrderByDescending(one => one.DetectedAt).First().Detail),
                StringComparer.OrdinalIgnoreCase);

        // Only the delayed ones. Every milestone of every shipment would be the
        // whole journey table, read to find the few rows that say anything.
        var stages = await db.ShipmentMilestones.AsNoTracking()
            .Where(stage => stage.Status == DelayedStage)
            .Select(stage => new { stage.JobKey, stage.DelayReason, stage.Remark })
            .ToListAsync(token);

        var stalled = stages
            .GroupBy(stage => stage.JobKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (Count: group.Count(),
                    Note: group.Select(one => Formats.Clean(one.DelayReason))
                        .Concat(group.Select(one => Formats.Clean(one.Remark)))
                        .FirstOrDefault(text => text.Length > 0) ?? ""),
                StringComparer.OrdinalIgnoreCase);

        var records = snapshot.Rows
            .Select(row => row.Record)
            .Where(record => record is not null && record.Identity.Length > 0)
            .Select(record => record!)
            .ToList();

        var found = new List<(ProblemRules.Row Row, JobRecord Job)>();
        foreach (var record in records)
        {
            var key = record.Identity;
            var delay = open.GetValueOrDefault(key);
            var stage = stalled.GetValueOrDefault(key);
            var judged = ProblemRules.Judge(record,
                new ProblemRules.Recorded(delay.Count, delay.Note ?? "", stage.Count, stage.Note ?? ""));
            if (judged is not null) found.Add((judged.Value, record));
        }

        var counted = ProblemRules.Count(records, found.Select(pair => pair.Row).ToList());

        // The register row travels with its judgement rather than being looked
        // up again afterwards: the two readings of a job's key agree today, and
        // a lookup that ever missed would hand back a default view whose strings
        // are null rather than empty.
        var rows = ProblemRules.InReadingOrder(found, pair => pair.Row)
            .Select(pair => new ProblemRow(
                pair.Row.Key,
                pair.Row.Problems.Select(one => one.ToString()).ToList(),
                pair.Row.Problems.Select(ProblemRules.Thai).ToList(),
                pair.Row.MinutesLate, pair.Row.Measurable,
                pair.Row.Note, ProblemRules.Thai(pair.Row.NoteFrom),
                pair.Job.Date, pair.Job.Customer, pair.Job.Trucker, pair.Job.Op,
                pair.Job.Status, pair.Job.JobCode,
                Moment(pair.Job.Date, pair.Job.PlanTime),
                Moment(pair.Job.ArrDate, pair.Job.ArrTime)))
            .ToList();

        return (rows, new ProblemTally(counted.Live, counted.WithProblem, counted.Unmeasurable,
            counted.ArrivedLate, JobRules.LateMinutes));
    }

    /// <summary>The milestone status an operator sets when a stage ran late.</summary>
    private const string DelayedStage = "delayed";

    /// <summary>
    /// A date and a time as one string, exactly as the register spells them.
    ///
    /// Not reformatted. If a plan time reads 00:30 because somebody meant 10:30,
    /// the point of showing it is that a supervisor sees 00:30.
    /// </summary>
    private static string Moment(string date, string time)
    {
        var day = Formats.Clean(date);
        var clock = Formats.Clean(time);
        if (day.Length == 0 && clock.Length == 0) return "";
        return clock.Length == 0 ? day : day.Length == 0 ? clock : day + " " + clock;
    }

    /// <summary>
    /// The morning, read into sentences for the dashboard.
    ///
    /// Built from the board this service already produces rather than from a
    /// second pass over the register: the briefing must never be able to
    /// disagree with the monitor it summarises, and the only way to guarantee
    /// that is for it to have counted nothing of its own.
    /// </summary>
    /// <param name="showTeam">
    /// Whether this reader may see whose backlog is whose. A briefing is still a
    /// view of the register, and naming who is carrying most of the risk list is
    /// team information.
    /// </param>
    public async Task<(IReadOnlyList<Briefing.Finding> Findings, string Quiet, string Today)>
        BriefAsync(bool showTeam, CancellationToken token)
    {
        var board = await ReadAsync(token);

        // The heaviest load, which is the only name the briefing ever uses.
        // Loads arrive already sorted with the most flagged first.
        var overdue = board.Risks.Count(risk => risk.Why == nameof(MonitorRules.Risk.Overdue));
        var busiest = board.Loads.FirstOrDefault();
        var worstParty = board.Blames.FirstOrDefault();

        var facts = new Briefing.Facts(
            Live: board.Tally.Live,
            // Split so the briefing cannot count the same job twice: on this
            // register every one of the 123 flagged jobs was overdue, and told
            // both figures it said 123 twice in two different sentences.
            Overdue: overdue,
            MissingBeforeRun: board.Risks.Count - overdue,
            WithProblem: board.Tally.WithProblem,
            ArrivedLate: board.Tally.ArrivedLate,
            LateMinutes: board.Tally.LateMinutes,
            Incidents: board.Problems.Count(row => row.Problems.Contains(nameof(ProblemRules.Problem.Incident))),
            OpenDelays: board.Problems.Count(row => row.Problems.Contains(nameof(ProblemRules.Problem.DelayOpen))),
            Unmeasurable: board.Tally.Unmeasurable,
            BusiestOwner: busiest?.Owner ?? "",
            BusiestOwnerFlagged: busiest?.Flagged ?? 0,
            TopDelayParty: worstParty?.Thai ?? "",
            TopDelayCases: worstParty?.Cases ?? 0,
            ShowTeam: showTeam);

        return (Briefing.Read(facts), Briefing.Quiet(facts), board.Today);
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
