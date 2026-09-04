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

    /// <summary>Cancelled, or moved off the date it was first planned for.</summary>
    public const string CancelMoved = "CANCEL / MOVED";

    /// <summary>In the order the strip draws them.</summary>
    public static readonly string[] All =
        [MyJobs, Pending, Today, Tomorrow, Delay, CancelMoved, DocumentMissing, Completed, Calendar];

    /// <summary>
    /// The work My Job counts.
    ///
    /// Import and export. Domestic is worked under The Chemours and its grid is
    /// there, so counting it here would be a figure whose rows nobody can open.
    /// Written here because the carrier scorecard has to count the same
    /// shipments the workspace does — a contract figure that disagrees with the
    /// screen the operators work from is a figure somebody has to reconcile by
    /// hand every month.
    /// </summary>
    public static bool CountedInWorkspace(string category) =>
        !string.Equals((category ?? "").Trim(), "DELIVERY", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Not happening. One reading of the status, in one place, because the
    /// question is now asked by four tab rules and the grid's colouring.
    /// </summary>
    public static bool IsCancelled(JobView job) => IsCancelled(job.Status);

    /// <summary>
    /// The same test for callers holding a <see cref="JobRecord"/> rather than a
    /// view. One reading, so a job cannot be cancelled on one screen and live on
    /// another.
    /// </summary>
    public static bool IsCancelled(string status) =>
        string.Equals(status.Trim(), JobStatus.Cancelled, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Moved at least once from the date it was first planned for.
    ///
    /// `origDate` is written the first time the plan date changes and never
    /// again, so this stays true for the rest of the job's life however many
    /// times it moves afterwards — which is the point. A customer who has moved
    /// one shipment four times should not fall off this list on the fifth.
    /// </summary>
    public static bool WasMoved(JobView job)
    {
        var from = job.OrigDate.Trim();
        return from.Length > 0 && !string.Equals(from, job.Date.Trim(), StringComparison.Ordinal);
    }

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

        // A cancelled job is not work waiting to be done, and it used to sit in
        // PENDING for the rest of its life looking like some — IsDone answers
        // only for COMPLETED, so nothing ever took it out. It keeps its place in
        // MY JOBS and CALENDAR, where the questions are what belongs to whom and
        // what was planned, and leaves the lists that mean "still to do".
        Pending => !JobRules.IsDone(job.Status) && !IsCancelled(job),
        Today => job.Date == Formats.PlanDate(today) && !IsCancelled(job),
        Tomorrow => job.Date == Formats.PlanDate(today.AddDays(1)) && !IsCancelled(job),

        // A delay is the status saying so, or a reason somebody wrote down. Not
        // "action required" — that bucket is mostly missing values, and calling
        // those delays would put seventeen hundred jobs behind a word that is
        // supposed to mean something.
        //
        // A postponement is not a delay either: a delay is a job that missed its
        // plan, a postponement is a plan that changed before it was missed. Two
        // different conversations with two different people, so two tabs.
        Delay => (JobRules.IsDelayed(job.Status) || job.Reason.Trim().Length > 0) && !IsCancelled(job),

        CancelMoved => IsCancelled(job) || WasMoved(job),

        DocumentMissing => IsDocumentMissing(job) && !IsCancelled(job),
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
        /// <summary>Where the job was first planned for, written once when the date moves.</summary>
        string OrigDate, string MoveReason, string MoveBy, string CancelReason,
        string Trucker, string JobCode,
        /// <summary>The operator's display name, which the assignee picker matches on.</summary>
        string Owner,
        string Abs, string Booking, string Licence, string Driver,
        string Destination, string Sid,
        /// <summary>
        /// When the lorry actually turned up. Appended rather than placed
        /// beside Date: this is a positional record, and an argument inserted
        /// in the middle shifts every one after it into the wrong field — a
        /// mistake that compiles, because they are all strings.
        /// </summary>
        string ArrDate, string ArrTime,
        JsonElement Raw)
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
                Text(row, "seal"), Text(row, "customer"),
                Text(row, "origDate"), Text(row, "moveReason"), Text(row, "moveBy"), Text(row, "cancelReason"),
                Text(row, "trucker"), Text(row, "jobCode"),
                Text(row, "op"), Text(row, "abs"), Text(row, "booking"), Text(row, "licence"),
                Text(row, "driver"), Text(row, "destination"), Text(row, "sid"),
                Text(row, "arrDate"), Text(row, "arrTime"),
                row);
        }
    }
}
