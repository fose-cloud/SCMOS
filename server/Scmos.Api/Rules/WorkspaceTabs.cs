using System.Text.Json;

namespace Scmos.Api.Rules;

/// <summary>
/// What each tab of the workspace means.
///
/// These rules lived in the browser, which was fine while the browser held the
/// whole register. It cannot keep holding it — two and a half megabytes on every
/// load, most of it work belonging to other people — so the filtering moves
/// here, and the rules have to come with it.
///
/// They are <b>moved</b>, not copied. Every rule this project has written twice
/// has drifted: a delay counted 2 in one place and 64 in another, an edit check
/// existed in three files and disagreed in all three. The browser now asks this
/// for its rows and its counts, so a tab cannot say 419 and then show a
/// different set.
/// </summary>
public static class WorkspaceTabs
{
    public const string MyJobs = "MY JOBS";
    public const string Pending = "PENDING";
    public const string Today = "TODAY";
    public const string Tomorrow = "TOMORROW";
    public const string Delay = "DELAY";
    public const string DocumentMissing = "DOCUMENT MISSING";
    public const string Completed = "COMPLETED";
    public const string Calendar = "CALENDAR";

    /// <summary>In the order the strip draws them.</summary>
    public static readonly string[] All =
        [MyJobs, Pending, Today, Tomorrow, Delay, DocumentMissing, Completed, Calendar];

    /// <summary>
    /// Whether one job belongs on one tab.
    ///
    /// <paramref name="today"/> is passed rather than read from the clock so a
    /// count and the page of rows behind it cannot straddle midnight and
    /// disagree.
    /// </summary>
    public static bool Matches(string tab, JobView job, string opId, DateOnly today) => tab switch
    {
        MyJobs => opId.Length > 0 && string.Equals(job.OwnerId, opId, StringComparison.OrdinalIgnoreCase),
        Pending => !JobRules.IsDone(job.Status),
        Today => job.Date == Formats.PlanDate(today),
        Tomorrow => job.Date == Formats.PlanDate(today.AddDays(1)),

        // A delay is the status saying so, or a reason somebody wrote down. Not
        // "action required" — that bucket is mostly missing values, and calling
        // those delays would put seventeen hundred jobs behind a word that is
        // supposed to mean something.
        Delay => JobRules.IsDelayed(job.Status) || job.Reason.Trim().Length > 0,

        DocumentMissing => IsDocumentMissing(job),
        Completed => JobRules.IsDone(job.Status),

        // The calendar is not a filter over jobs; it is a list of dates. Every
        // job with a date belongs to it.
        Calendar => job.Date.Length > 0,
        _ => true,
    };

    /// <summary>
    /// Paperwork that should be on the job and is not.
    ///
    /// A finished job is never listed: chasing a container number for work that
    /// has already been delivered is noise, and it is the reason this bucket was
    /// trusted enough to act on.
    /// </summary>
    public static bool IsDocumentMissing(JobView job)
    {
        if (JobRules.IsDone(job.Status)) return false;

        // Vehicles that carry no container cannot be missing one.
        var carriesContainer = !System.Text.RegularExpressions.Regex.IsMatch(
            job.Type ?? "", "6WH|4WH|10W|COMBINE", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (carriesContainer && job.Container.Trim().Length == 0) return true;

        return string.Equals(job.Cat, "EXPORT", StringComparison.OrdinalIgnoreCase)
               && job.Seal.Trim().Length == 0;
    }

    /// <summary>
    /// The fields the tab rules and the grid actually read, lifted out of the
    /// stored JSON once per job rather than parsed again for every question.
    /// </summary>
    public readonly record struct JobView(
        string Key, string Cat, string OwnerId, string Date, string Status,
        string Reason, string Type, string Container, string Seal, string Customer,
        string Trucker, string JobCode, JsonElement Raw)
    {
        public static JobView From(JsonElement row)
        {
            static string Text(JsonElement e, string name) =>
                e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString() ?? "" : "";

            // The browser reads a job's key as `key ?? id`, and a couple of rows
            // in the register carry only the second. Skipping those here left
            // PENDING two short of what the workspace has always shown —
            // small enough to look like a rounding difference and wrong enough
            // to mean the two sides disagree about how many jobs exist.
            var key = Text(row, "key");
            if (key.Length == 0) key = Text(row, "id");

            return new JobView(
                key, Text(row, "cat"), Text(row, "opId"), Text(row, "date"),
                Text(row, "status"), Text(row, "reason"), Text(row, "type"), Text(row, "container"),
                Text(row, "seal"), Text(row, "customer"), Text(row, "trucker"), Text(row, "jobCode"),
                row);
        }
    }
}
