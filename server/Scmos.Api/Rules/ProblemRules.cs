namespace Scmos.Api.Rules;

/// <summary>
/// What is wrong with a shipment right now.
///
/// <para>
/// The monitor already answers "what needs somebody today" — that is
/// <see cref="MonitorRules"/>, and it is deliberately narrow: four kinds, all of
/// them a missing piece on a job that has not run yet, and a job whose lorry
/// turned up drops off it entirely. That narrowness is the reason it is worth
/// reading, and nothing here widens it.
/// </para>
/// <para>
/// This is the other question, the one the supervisor asked for: of the work in
/// flight, which of it has gone wrong, and what went wrong. Different jobs,
/// mostly — a shipment that arrived four hours late has no missing piece at all,
/// and until now the only way to learn about it was to open that one job.
/// </para>
/// <para>
/// Every kind below is either <b>measured</b> or <b>declared by a person</b>.
/// Nothing here reads free text and decides what it means: where an operator
/// wrote something, their words are carried through and shown, and what puts the
/// job on the list is that they wrote them — not what they say.
/// </para>
/// </summary>
public static class ProblemRules
{
    /// <summary>What went wrong, most serious first.</summary>
    public enum Problem
    {
        /// <summary>
        /// Something is written in the INCIDENT REPORT column.
        ///
        /// First because it is the rarest and the loudest: seven rows in the
        /// July plan, every one an operator stopping to type what happened. A
        /// container rejected on inspection, a card in the wrong box, a line
        /// waiting on the shipping company — none of which any other rule on
        /// this list would ever have found.
        /// </summary>
        Incident,

        /// <summary>
        /// A delay was recorded, categorised, and nobody has closed it.
        ///
        /// The delay register knows who is answerable and how much time went.
        /// What it has never done is say so anywhere except inside the one job
        /// it belongs to.
        /// </summary>
        DelayOpen,

        /// <summary>A milestone an operator marked "delayed" as the run went past it.</summary>
        StageDelayed,

        /// <summary>
        /// Measured: the lorry arrived more than <see cref="JobRules.LateMinutes"/>
        /// minutes after the plan said it would.
        ///
        /// Plan date and time against arrival date and time, through
        /// <see cref="JobRules.MinutesLate"/> — the same reading the carrier
        /// scorecard is built on, so a shipment cannot be late on one screen and
        /// on time on another.
        /// </summary>
        ArrivedLate,

        /// <summary>
        /// A held status, or a reason typed into the Reason / Delay column.
        ///
        /// <see cref="JobRules.WasDelayed"/>, which is what the workspace's own
        /// DELAY tab has always counted. Weakest of the five and last for that
        /// reason: the column collects progress notes as well as delays, so the
        /// text is shown beside it and a supervisor can see at a glance which of
        /// the two they are looking at.
        /// </summary>
        DelayNoted,
    }

    /// <summary>Where the words on a row came from, so the screen can say so.</summary>
    public enum Source { None, Incident, DelayRecord, Milestone, Reason }

    /// <summary>
    /// What the delay register and the milestone table know about one job.
    ///
    /// Passed in rather than read here, so the whole judgement stays pure and
    /// <c>--check-problems</c> can run it without a database.
    /// </summary>
    /// <param name="OpenDelays">Delay records with nothing recorded as closing them.</param>
    /// <param name="OpenDelayNote">The most recent of those, in the operator's words.</param>
    /// <param name="DelayedStages">Milestones marked delayed.</param>
    /// <param name="DelayedStageNote">The reason given on one of them.</param>
    public readonly record struct Recorded(
        int OpenDelays, string OpenDelayNote, int DelayedStages, string DelayedStageNote)
    {
        public static readonly Recorded Nothing = new(0, "", 0, "");
    }

    /// <param name="Key">The job, so a row can open it.</param>
    /// <param name="Problems">Everything wrong with it, most serious first.</param>
    /// <param name="MinutesLate">
    /// How late the arrival was, or zero when it was not late. Zero also when it
    /// could not be measured, which is why <c>Measurable</c> sits beside it
    /// rather than being folded in.
    /// </param>
    /// <param name="Measurable">
    /// Whether plan and arrival were both recorded well enough to compare.
    ///
    /// False on a hundred of the July plan's three hundred and seventy live
    /// jobs. A screen that quietly counted those as on time would be reporting
    /// the gaps in its own records as good news.
    /// </param>
    /// <param name="Note">The operator's own words, unedited.</param>
    /// <param name="NoteFrom">Which column or table they wrote them in.</param>
    public readonly record struct Row(
        string Key, IReadOnlyList<Problem> Problems, int MinutesLate, bool Measurable,
        string Note, Source NoteFrom)
    {
        /// <summary>The most serious thing wrong, which is what the list sorts on.</summary>
        public Problem Worst => Problems[0];
    }

    /// <summary>
    /// Everything wrong with this job, or null when nothing is.
    ///
    /// Finished and cancelled work is never judged. A shipment delivered late
    /// last week is a fact for the scorecard and the KPI to argue about; it is
    /// not something a supervisor can do anything about this morning, and a list
    /// that fills up with it stops being read.
    /// </summary>
    public static Row? Judge(JobRecord job, Recorded recorded)
    {
        if (JobRules.IsDone(job.Status) || WorkspaceTabs.IsCancelled(job.Status)) return null;

        var problems = new List<Problem>();

        var incident = Formats.Clean(job.Incident);
        if (incident.Length > 0) problems.Add(Problem.Incident);
        if (recorded.OpenDelays > 0) problems.Add(Problem.DelayOpen);
        if (recorded.DelayedStages > 0) problems.Add(Problem.StageDelayed);

        // Measured once and read three times below. The threshold is JobRules'
        // own, so this cannot drift away from what the scorecard calls late.
        var late = JobRules.MinutesLate(job);
        if (late > JobRules.LateMinutes) problems.Add(Problem.ArrivedLate);

        // Last, and only when nothing better has already spoken for this job: a
        // delay with a record or a marked stage behind it is described far
        // better by those than by "there is text in a column".
        if (problems.Count == 0 && JobRules.WasDelayed(job)) problems.Add(Problem.DelayNoted);

        if (problems.Count == 0) return null;

        var (note, from) = Words(job, recorded, incident);
        return new Row(
            job.Identity,
            problems,
            late is > 0 ? (int)Math.Round(late.Value) : 0,
            late is not null,
            note,
            from);
    }

    /// <summary>
    /// The words to show, and where they came from.
    ///
    /// In the order of how much somebody meant them: an incident report was
    /// typed on purpose, a delay record was categorised as well as typed, a
    /// milestone reason was given at the moment it happened, and the Reason
    /// column is whatever was to hand.
    /// </summary>
    private static (string Note, Source From) Words(JobRecord job, Recorded recorded, string incident)
    {
        if (incident.Length > 0) return (incident, Source.Incident);

        var delay = Formats.Clean(recorded.OpenDelayNote);
        if (delay.Length > 0) return (delay, Source.DelayRecord);

        var stage = Formats.Clean(recorded.DelayedStageNote);
        if (stage.Length > 0) return (stage, Source.Milestone);

        var reason = Formats.Clean(job.Reason);
        return reason.Length > 0 ? (reason, Source.Reason) : ("", Source.None);
    }

    /// <summary>
    /// The order a supervisor should read them in: the most serious kind first,
    /// and within a kind the one that lost the most time.
    /// </summary>
    public static IOrderedEnumerable<Row> InReadingOrder(IEnumerable<Row> rows) =>
        InReadingOrder(rows, row => row);

    /// <summary>
    /// The same order for a caller carrying each row alongside the register row
    /// it was judged from, so the pair can be sorted without being taken apart
    /// and matched up again afterwards.
    /// </summary>
    public static IOrderedEnumerable<T> InReadingOrder<T>(IEnumerable<T> items, Func<T, Row> row) =>
        items.OrderBy(one => (int)row(one).Worst)
            .ThenByDescending(one => row(one).MinutesLate)
            .ThenByDescending(one => row(one).Problems.Count);

    /// <param name="Live">Jobs neither finished nor cancelled — the work in flight.</param>
    /// <param name="WithProblem">How many of those have at least one thing wrong.</param>
    /// <param name="Unmeasurable">
    /// Live jobs whose lateness cannot be worked out, because the plan time or
    /// the arrival was never filled in.
    ///
    /// Counted and shown rather than left out. It is the number that says how far
    /// the rest of this screen can be trusted, and leaving it off would let a
    /// thin morning read as a quiet one.
    /// </param>
    /// <param name="ArrivedLate">How many of the problems are a measured late arrival.</param>
    public readonly record struct Tally(int Live, int WithProblem, int Unmeasurable, int ArrivedLate);

    /// <summary>The headline, counted over the whole register rather than a page of it.</summary>
    public static Tally Count(IEnumerable<JobRecord> jobs, IReadOnlyList<Row> problems)
    {
        var live = jobs
            .Where(job => !JobRules.IsDone(job.Status) && !WorkspaceTabs.IsCancelled(job.Status))
            .ToList();

        return new Tally(
            live.Count,
            problems.Count,
            live.Count(job => JobRules.MinutesLate(job) is null),
            problems.Count(row => row.Problems.Contains(Problem.ArrivedLate)));
    }

    /// <summary>
    /// The Thai the screens use, kept here so the monitor, the export and
    /// anything added later call the same thing by the same name.
    /// </summary>
    public static string Thai(Problem problem) => problem switch
    {
        Problem.Incident => "มีเหตุผิดปกติ",
        Problem.DelayOpen => "ความล่าช้ายังไม่ปิดเรื่อง",
        Problem.StageDelayed => "ขั้นตอนล่าช้า",
        Problem.ArrivedLate => "ถึงช้ากว่าแผน",
        Problem.DelayNoted => "มีบันทึกความล่าช้า",
        _ => problem.ToString(),
    };

    public static string Thai(Source source) => source switch
    {
        Source.Incident => "Incident Report",
        Source.DelayRecord => "บันทึกความล่าช้า",
        Source.Milestone => "ขั้นตอนการขนส่ง",
        Source.Reason => "ช่อง Reason / Delay",
        _ => "",
    };
}
