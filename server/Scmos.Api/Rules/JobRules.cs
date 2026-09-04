using System.Text.RegularExpressions;

namespace Scmos.Api.Rules;

public enum Severity { Error, Warning }

public record Issue(string Field, string Label, string Value, string Message, string Expected, Severity Severity);

/// <summary>
/// What makes a job valid, and which bucket its status falls in.
///
/// Ported from app/scmos/ops.ts and standard.ts. Two things depend on this being
/// exact: a job with an error-severity issue is kept out of the KPIs, and the
/// status buckets are what every count on every summary is grouped by. Getting
/// either slightly wrong changes numbers the team reports upward.
/// </summary>
public static partial class JobRules
{
    // These read a status written the old way, before the controlled codes. They
    // are anchored, and that is the whole point: an unanchored `complet` calls
    // "Not completed" finished, and an unanchored `delivered` calls "Partially
    // delivered" finished too. The browser has been anchored for some time and
    // carries the reasoning — DELIVERED means the goods arrived and the
    // paperwork has not, so treating it as done reported 228 running jobs as
    // complete. This side had drifted looser, which mattered because the KPI
    // engine reads IsDone: `gate-in` was listed as finished here while the
    // workspace counted it as running, so the same job was complete on one
    // screen and in transit on another.
    [GeneratedRegex(@"^(waiting truck|waiting information|new|scheduled)$", RegexOptions.IgnoreCase)]
    private static partial Regex Waiting();

    [GeneratedRegex(@"^(truck confirmed|driver assigned|truck assigned)$", RegexOptions.IgnoreCase)]
    private static partial Regex Confirmed();

    [GeneratedRegex(@"transit|arrived|loading|pickup|departed|gate|empty return", RegexOptions.IgnoreCase)]
    private static partial Regex Running();

    [GeneratedRegex(@"delay", RegexOptions.IgnoreCase)]
    private static partial Regex Delayed();

    [GeneratedRegex(@"^(completed|delivery completed|delivered)$", RegexOptions.IgnoreCase)]
    private static partial Regex Done();

    [GeneratedRegex(@"6WH|4WH|10W|COMBINE", RegexOptions.IgnoreCase)]
    private static partial Regex NoContainerNeeded();

    /// <summary>
    /// The buckets every summary groups by.
    ///
    /// Both spellings are accepted while the register holds a mix: the codes
    /// answer directly, and the regexes still read a job written the old way, so
    /// a workbook imported before the move is not silently uncounted.
    /// </summary>
    private static string Code(string status) => Formats.Clean(status).ToUpperInvariant();

    public static bool IsWaiting(string status) =>
        JobStatus.IsControlled(Code(status)) ? JobStatus.IsWaiting(Code(status)) : Waiting().IsMatch(status);

    public static bool IsConfirmed(string status) =>
        JobStatus.IsControlled(Code(status)) ? JobStatus.IsConfirmed(Code(status)) : Confirmed().IsMatch(status);

    public static bool IsRunning(string status) =>
        JobStatus.IsControlled(Code(status)) ? JobStatus.IsRunning(Code(status)) : Running().IsMatch(status);

    /// <summary>
    /// Whether a job is currently held or delayed, by its status alone.
    ///
    /// Narrower than what the workspace calls delayed — see
    /// <see cref="WasDelayed"/>, which is the definition the screens use and the
    /// one to reach for when counting.
    /// </summary>
    public static bool IsDelayed(string status) =>
        JobStatus.IsControlled(Code(status)) ? JobStatus.IsHeld(Code(status)) : Delayed().IsMatch(status);

    /// <summary>
    /// Whether this job ran late, by everything the register knows.
    ///
    /// A held status, or a delay reason somebody wrote down. The second half
    /// matters more than it looks: a job delayed on Tuesday and delivered on
    /// Wednesday is no longer held, and its only trace is the reason an operator
    /// typed. Counting the status alone found 2 delays where the workspace's
    /// DELAY tab — which has always used both — showed 64. The same word meant
    /// two things thirty-fold apart on two screens of the same app.
    /// </summary>
    public static bool WasDelayed(JobRecord job) =>
        IsDelayed(job.Status) || Formats.Clean(job.Reason).Length > 0;

    public static bool IsDone(string status) =>
        JobStatus.IsControlled(Code(status)) ? JobStatus.IsDone(Code(status)) : Done().IsMatch(status);

    public static string[] StatusesFor(string category) => JobStatus.For(category);

    /// <summary>
    /// Every value on the job that breaks the standard.
    ///
    /// An empty field is not an issue — the plan arrives with gaps and that is
    /// what the missing-information panels are for. This only judges values that
    /// are present and will not parse, because those are the ones that look like
    /// data and are not.
    /// </summary>
    public static List<Issue> Validate(JobRecord job)
    {
        var issues = new List<Issue>();
        var category = job.Cat.Length > 0 ? job.Cat : "IMPORT";

        void Check(string field, string label, string? raw, Func<string, bool> test, string expected,
            Severity severity = Severity.Error, string[]? onlyFor = null)
        {
            if (onlyFor is not null && !onlyFor.Contains(category, StringComparer.OrdinalIgnoreCase)) return;
            var value = Formats.Clean(raw);
            if (value.Length == 0 || test(value)) return;
            issues.Add(new Issue(field, label, value, "รูปแบบไม่ถูกต้อง", expected, severity));
        }

        Check("date", "Plan date", job.Date, Formats.IsDate, "DD/MM/YYYY");
        Check("planTime", "Plan loading time", job.PlanTime, Formats.IsTime, "HH:MM");
        Check("arrDate", "Arrival date", job.ArrDate, Formats.IsDate, "DD/MM/YYYY");
        Check("arrTime", "Arrival time", job.ArrTime, Formats.IsTime, "HH:MM");
        Check("closingDate", "Closing date", job.ClosingDate, Formats.IsDate, "DD/MM/YYYY", onlyFor: ["EXPORT"]);
        Check("closingTime", "Closing time", job.ClosingTime, Formats.IsTime, "HH:MM", onlyFor: ["EXPORT"]);
        Check("container", "Container no.", job.Container, Formats.IsContainer, "ตัวอักษร 4 ตัว + ตัวเลข 7 ตัว");
        Check("weight", "Weight (kg)", job.Weight, Formats.IsNumber, "ตัวเลขล้วน หน่วยกิโลกรัม");
        Check("contact", "Driver contact", job.Contact, Formats.IsPhone, "0XX-XXXXXXX");
        Check("licence", "Truck licence", job.Licence, Formats.IsPlate, "ทะเบียนไทย");

        var status = Formats.Clean(job.Status);
        if (status.Length > 0 && !StatusesFor(category).Contains(status, StringComparer.OrdinalIgnoreCase))
        {
            issues.Add(new Issue("status", "Status", status,
                $"ไม่อยู่ในชุดสถานะของงาน {category}", $"เลือกจากรายการสถานะ {category}", Severity.Error));
        }

        // An arrival time without its date cannot be placed on the calendar, so
        // the on-time calculation would silently skip the job.
        if (Formats.Clean(job.ArrTime).Length > 0 && Formats.Clean(job.ArrDate).Length == 0)
        {
            issues.Add(new Issue("arrDate", "Arrival date", "", "มีเวลาถึงแต่ไม่มีวันที่ถึง",
                "DD/MM/YYYY", Severity.Warning));
        }

        return issues;
    }

    /// <summary>True when every value on the job parses, so it can feed the dashboard.</summary>
    public static bool IsKpiReady(JobRecord job) => !Validate(job).Any(issue => issue.Severity == Severity.Error);

    /// <summary>
    /// The fields the grid colours a job for missing. Delivery jobs are exempt —
    /// they are run by LESCHACO's own fleet and carry none of this.
    /// </summary>
    public static List<string> Gaps(JobRecord job)
    {
        var gaps = new List<string>();
        if (job.Cat.Equals("DELIVERY", StringComparison.OrdinalIgnoreCase)) return gaps;

        if (Formats.Clean(job.Trucker).Length == 0) gaps.Add("Trucking company missing");
        if (Formats.Clean(job.Licence).Length == 0) gaps.Add("Licence missing");
        if (Formats.Clean(job.Driver).Length == 0) gaps.Add("Driver missing");
        if (Formats.Clean(job.Contact).Length == 0) gaps.Add("Driver contact missing");
        if (Formats.Clean(job.Container).Length == 0 && !NoContainerNeeded().IsMatch(job.Type))
            gaps.Add("Container missing");
        if (job.Cat.Equals("EXPORT", StringComparison.OrdinalIgnoreCase) && Formats.Clean(job.Seal).Length == 0)
            gaps.Add("Seal missing");
        if (Formats.Clean(job.ArrTime).Length == 0) gaps.Add("Arrival time missing");

        return gaps;
    }

    /// <summary>
    /// A job that needs a person: a value that will not parse, or a gap on a job
    /// that has not finished. A malformed value counts even on a finished job,
    /// because it keeps that job out of the KPIs.
    /// </summary>
    public static bool NeedsAction(JobRecord job)
    {
        if (!IsKpiReady(job)) return true;
        return !IsDone(job.Status) && Gaps(job).Count > 0;
    }

    /// <summary>
    /// An export whose container must be at the port before the yard closes, but
    /// whose truck is not due until after it. 109 of them in the July plan.
    /// </summary>
    public static bool GateInRisk(JobRecord job)
    {
        if (!job.Cat.Equals("EXPORT", StringComparison.OrdinalIgnoreCase)) return false;
        var closing = Formats.TimeMinutes(job.ClosingTime);
        var arrival = Formats.TimeMinutes(Formats.Clean(job.ArrTime).Length > 0 ? job.ArrTime : job.PlanTime);
        return closing is not null && arrival is not null && closing - arrival < 0;
    }

    /// <summary>
    /// On-time is only measurable when the plan and the arrival both parse. The
    /// base travels with the figure everywhere it is shown, because 55% of 630
    /// is a different claim from 55% of 2,102.
    /// </summary>
    /// <summary>
    /// Whether a DD/MM/YYYY date falls in the period being reported on.
    ///
    /// Here rather than in each service. The operational report and the measures
    /// engine each had their own copy of these four lines, which is the shape of
    /// bug this codebase keeps finding: two readings of one rule that agree
    /// until the day somebody adjusts one of them.
    ///
    /// Takes a date rather than a job, because an operational issue is dated by
    /// when it was found and some of them never reach a job at all.
    /// </summary>
    public static bool InPeriod(string date, string year, string month, string day)
    {
        if (year.Length == 0 && month.Length == 0 && day.Length == 0) return true;
        var (jobYear, jobMonth, jobDay) = Formats.PartsOf(date);
        if (jobYear.Length == 0) return false;
        if (year.Length > 0 && year != jobYear) return false;
        if (month.Length > 0 && month != jobMonth) return false;
        if (day.Length > 0 && day != jobDay) return false;
        return true;
    }

    public static bool IsMeasurable(JobRecord job) =>
        Formats.TimeMinutes(job.PlanTime) is not null
        && Formats.TimeMinutes(job.ArrTime) is not null
        && Formats.DateNumber(job.Date) > 0
        && Formats.DateNumber(job.ArrDate) > 0;

    /// <summary>
    /// How many minutes after its plan the shipment arrived, or null when it
    /// cannot be measured. Negative when it arrived early.
    ///
    /// Beside IsOnTime rather than worked out again wherever somebody needs a
    /// threshold: the carrier scorecard counts arrivals more than thirty
    /// minutes late, and a second reading of "late" would disagree with this
    /// one the first time one of them was adjusted.
    /// </summary>
    public static double? MinutesLate(JobRecord job)
    {
        if (!IsMeasurable(job)) return null;
        var planned = Formats.Moment(job.Date, job.PlanTime);
        var arrived = Formats.Moment(job.ArrDate, job.ArrTime);
        if (planned is null || arrived is null) return null;
        return (arrived.Value - planned.Value).TotalMinutes;
    }

    /// <summary>
    /// Late by more than this many minutes and the shipment is not on time.
    ///
    /// Thirty, which is the figure the carrier scorecard has always used — it
    /// lived there as a private constant, and the supervisor monitor needed the
    /// same judgement. Two copies of "late" is the shape of bug this codebase
    /// keeps finding, so there is one, here, beside the measurement it applies
    /// to.
    /// </summary>
    public const int LateMinutes = 30;

    /// <summary>
    /// Whether the shipment arrived more than <paramref name="minutes"/> after
    /// its plan. False when it cannot be measured — which is not the same as on
    /// time, and no caller may read it as such.
    /// </summary>
    public static bool LateBeyond(JobRecord job, int minutes = LateMinutes)
    {
        var late = MinutesLate(job);
        return late is not null && late > minutes;
    }

    public static bool IsOnTime(JobRecord job)
    {
        if (!IsMeasurable(job)) return false;
        var planned = Formats.DateNumber(job.Date);
        var arrived = Formats.DateNumber(job.ArrDate);
        if (arrived < planned) return true;
        return arrived == planned && Formats.TimeMinutes(job.ArrTime) <= Formats.TimeMinutes(job.PlanTime);
    }
}
