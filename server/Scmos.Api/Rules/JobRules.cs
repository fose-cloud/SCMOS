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
    [GeneratedRegex(@"waiting truck|new|scheduled", RegexOptions.IgnoreCase)]
    private static partial Regex Waiting();

    [GeneratedRegex(@"truck confirmed|driver assigned|truck assigned", RegexOptions.IgnoreCase)]
    private static partial Regex Confirmed();

    [GeneratedRegex(@"transit|arrived|loading|pickup|departed|gate", RegexOptions.IgnoreCase)]
    private static partial Regex Running();

    [GeneratedRegex(@"delay", RegexOptions.IgnoreCase)]
    private static partial Regex Delayed();

    [GeneratedRegex(@"complet|delivered|gate-in", RegexOptions.IgnoreCase)]
    private static partial Regex Done();

    [GeneratedRegex(@"6WH|4WH|10W|COMBINE", RegexOptions.IgnoreCase)]
    private static partial Regex NoContainerNeeded();

    public static bool IsWaiting(string status) => Waiting().IsMatch(status);
    public static bool IsConfirmed(string status) => Confirmed().IsMatch(status);
    public static bool IsRunning(string status) => Running().IsMatch(status);
    public static bool IsDelayed(string status) => Delayed().IsMatch(status);
    public static bool IsDone(string status) => Done().IsMatch(status);

    /// <summary>The status ladder a category may use. Kept in step with theme.ts.</summary>
    public static readonly Dictionary<string, string[]> StatusLadder = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IMPORT"] =
        [
            "New", "Waiting Information", "Waiting Truck", "Truck Confirmed", "Driver Assigned",
            "Container Pickup", "Departed Port", "In Transit", "Arrived Customer", "Delivery Started",
            "Delivery Completed", "Empty Return Pending", "Empty Returned", "Completed", "Delayed", "Cancelled",
        ],
        ["EXPORT"] =
        [
            "New", "Waiting Information", "Waiting Truck", "Truck Confirmed", "Empty Pickup",
            "Driver Assigned", "Arrived Plant", "Loading", "Loading Completed", "Departed Plant",
            "Port Return", "Gate-In Completed", "Completed", "Delayed", "Cancelled",
        ],
        ["DELIVERY"] =
        [
            "Scheduled", "Truck Assigned", "Pickup", "In Transit", "Delivered", "Completed", "Delayed", "Cancelled",
        ],
    };

    public static string[] StatusesFor(string category) =>
        StatusLadder.TryGetValue(category, out var ladder) ? ladder : StatusLadder["IMPORT"];

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
    public static bool IsMeasurable(JobRecord job) =>
        Formats.TimeMinutes(job.PlanTime) is not null
        && Formats.TimeMinutes(job.ArrTime) is not null
        && Formats.DateNumber(job.Date) > 0
        && Formats.DateNumber(job.ArrDate) > 0;

    public static bool IsOnTime(JobRecord job)
    {
        if (!IsMeasurable(job)) return false;
        var planned = Formats.DateNumber(job.Date);
        var arrived = Formats.DateNumber(job.ArrDate);
        if (arrived < planned) return true;
        return arrived == planned && Formats.TimeMinutes(job.ArrTime) <= Formats.TimeMinutes(job.PlanTime);
    }
}
