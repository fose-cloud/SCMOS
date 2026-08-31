namespace Scmos.Api.Rules;

/// <summary>
/// What a supervisor is watching for.
///
/// The shipment monitor used to answer "where is this journey", which is the
/// question of whoever is carrying it. A supervisor asks three different ones:
/// what is about to go wrong today, who is carrying too much, and where the
/// month's time went. The first two are decided here.
///
/// <para>
/// Pure, and taking today as an argument, so the answers can be checked
/// without a register, a clock or a session — `--check-monitor` runs them. The
/// risk list in particular is a judgement about somebody's day, and a rule
/// that quietly widens is a rule that fills the screen with rows nobody acts
/// on until they stop reading it.
/// </para>
/// </summary>
public static class MonitorRules
{
    /// <summary>Why a job is on the list, most serious first.</summary>
    public enum Risk
    {
        /// <summary>Past its plan time with nothing recorded as having arrived.</summary>
        Overdue,

        /// <summary>
        /// Nobody owns it, so nobody is looking at it.
        ///
        /// Second because it is the only kind on this list that nobody is
        /// working on by definition. A job missing a lorry at least has
        /// somebody whose job it is to find one.
        /// </summary>
        Unassigned,

        /// <summary>Running within days and still nobody carrying it.</summary>
        NoCarrier,

        /// <summary>Carrier agreed, but no lorry or driver named yet.</summary>
        NoTruck,
    }

    /// <summary>
    /// How close a job has to be before missing pieces become urgent.
    ///
    /// Two days, because a carrier can still be found in two days and cannot be
    /// found in one. A longer window fills the list with jobs that are simply
    /// not due yet; a shorter one reports them when it is already too late.
    /// </summary>
    public const int SoonDays = 2;

    /// <param name="Key">The job, so the row can open it.</param>
    /// <param name="Why">What put it on the list.</param>
    /// <param name="DaysAway">Negative when the plan date has passed.</param>
    public readonly record struct Flag(string Key, Risk Why, int DaysAway);

    /// <summary>
    /// The reason this job needs somebody today, or null when it does not.
    ///
    /// One reason per job, the most serious that applies: a job with no carrier
    /// two days out also has no lorry, and listing it twice would make the list
    /// look twice as bad as the day is.
    /// </summary>
    public static Flag? Judge(WorkspaceTabs.JobView job, DateOnly today)
    {
        // Finished and cancelled work is not a problem to solve. Checked first
        // so nothing below has to remember to.
        if (JobRules.IsDone(job.Status) || WorkspaceTabs.IsCancelled(job)) return null;

        var planned = Formats.ParseDay(job.Date);
        if (planned is null) return null;

        var days = planned.Value.DayNumber - today.DayNumber;
        var arrived = job.ArrDate.Trim().Length > 0 || job.ArrTime.Trim().Length > 0;

        if (days < 0 && !arrived) return new Flag(job.Key, Risk.Overdue, days);
        if (job.Owner.Trim().Length == 0) return new Flag(job.Key, Risk.Unassigned, days);

        // Only once it is close. A job three weeks out with no carrier is a job
        // three weeks out, not a problem.
        if (days > SoonDays) return null;

        if (job.Trucker.Trim().Length == 0) return new Flag(job.Key, Risk.NoCarrier, days);
        if (job.Licence.Trim().Length == 0 && job.Driver.Trim().Length == 0)
            return new Flag(job.Key, Risk.NoTruck, days);

        return null;
    }

    /// <summary>
    /// The order a supervisor should read them in: the most serious kind first,
    /// and within a kind the one running soonest.
    ///
    /// Overdue before everything because it has already happened; an unassigned
    /// job before a missing lorry because nobody is even looking at it.
    /// </summary>
    public static IOrderedEnumerable<Flag> InReadingOrder(IEnumerable<Flag> flags) =>
        flags.OrderBy(flag => (int)flag.Why).ThenBy(flag => flag.DaysAway);

    /// <param name="Carrying">Jobs on this person that are not finished.</param>
    /// <param name="Flagged">How many of those need somebody today.</param>
    /// <param name="OldestDaysWaiting">
    /// Days since the plan date of the oldest unfinished job, or zero when
    /// nothing is late. What tells a supervisor whether a big number is a busy
    /// week or a backlog.
    /// </param>
    public readonly record struct Load(string OwnerId, string Owner, int Carrying, int Flagged,
        int OldestDaysWaiting);

    /// <summary>
    /// What each person is carrying.
    ///
    /// Counted from unfinished work only: a person who closed forty jobs this
    /// month is not carrying forty jobs, and a load board that says so sends a
    /// supervisor to help the wrong person.
    /// </summary>
    public static IReadOnlyList<Load> Loads(IEnumerable<WorkspaceTabs.JobView> jobs, DateOnly today)
    {
        var live = jobs
            .Where(job => !JobRules.IsDone(job.Status) && !WorkspaceTabs.IsCancelled(job))
            .ToList();

        return live
            .Where(job => job.OwnerId.Trim().Length > 0)
            .GroupBy(job => job.OwnerId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var overdue = group
                    .Select(job => Formats.ParseDay(job.Date))
                    .Where(day => day is not null && day.Value < today)
                    .Select(day => today.DayNumber - day!.Value.DayNumber)
                    .ToList();

                return new Load(
                    group.Key,
                    group.Select(job => job.Owner).FirstOrDefault(name => name.Length > 0) ?? group.Key,
                    group.Count(),
                    group.Count(job => Judge(job, today) is not null),
                    overdue.Count == 0 ? 0 : overdue.Max());
            })
            .OrderByDescending(load => load.Flagged)
            .ThenByDescending(load => load.Carrying)
            .ToList();
    }
}
