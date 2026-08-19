using System.Text.Json;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// One page of the register, chosen here rather than in the browser.
///
/// The workspace used to fetch every job — 2,626 of them, two and a half
/// megabytes — and then filter, sort and paginate twenty-five rows out of it.
/// Everyone downloaded everyone's work to look at their own, on every load, on
/// whatever connection they happened to be on.
///
/// This answers the question the screen is actually asking: given a tab, a
/// category, a period and a sort, what are the twenty-five rows to draw, how
/// many are there in total, and what numbers go on the tab strip. The counts
/// come from the same pass as the rows, so the strip cannot promise a number
/// the grid then fails to show.
/// </summary>
public class WorkspaceService(JobsRepository jobs)
{
    /// <param name="Assignee">
    /// "My Work", an operator's display name, or ALL. Ignored on the MY JOBS
    /// tab, which is already narrowed to one person.
    /// </param>
    /// <param name="Kpi">
    /// The drill-down from the panel above the grid: Mine, Imp, Exp, Del, Wait,
    /// Conf, Run, Delay, Done. Two of the browser's drills — Act and Fmt — are
    /// deliberately absent: they filter on validation computed in the browser,
    /// and answering them here means proving two validators agree first.
    /// </param>
    public record Query(
        string Tab, string Cat, string Year, string Month, string Day,
        string Search, string SortKey, string SortDir, int Page, int Per, string OpId,
        string Assignee, string Owner, string Customer, string Trucker, string Type,
        string Status, string Kpi);

    public record Page(
        IReadOnlyList<JsonElement> Rows,
        int Total,
        int PageCount,
        int CurrentPage,
        IReadOnlyDictionary<string, int> Counts,
        IReadOnlyList<string> Dates,
        DateTimeOffset UpdatedAt);

    public async Task<Page> ReadAsync(Query query, CancellationToken token)
    {
        var (json, _) = await jobs.LoadAsync(token);
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("jobs", out var all)
            || all.ValueKind != JsonValueKind.Array)
        {
            return new Page([], 0, 0, 1, new Dictionary<string, int>(), [], default);
        }

        var updatedAt = document.RootElement.TryGetProperty("updatedAt", out var stamp)
                        && stamp.ValueKind == JsonValueKind.String
                        && DateTimeOffset.TryParse(stamp.GetString(), out var parsed)
            ? parsed
            : default;

        // One "today" for the whole answer. Reading the clock per job would let
        // a request that straddles midnight count a job on TODAY and then leave
        // it off the page.
        var today = DateOnly.FromDateTime(DateTime.Now);

        // The category narrows everything, including the tab counts — the strip
        // shows what is on that tab *within* the category you are looking at,
        // which is what the browser did.
        var inCategory = new List<WorkspaceTabs.JobView>();
        foreach (var row in all.EnumerateArray())
        {
            var job = WorkspaceTabs.JobView.From(row);
            if (job.Key.Length == 0) continue;
            if (!MatchesCategory(job, query.Cat)) continue;
            if (!MatchesPeriod(job, query)) continue;
            inCategory.Add(job);
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var tab in WorkspaceTabs.All)
        {
            counts[tab] = tab == WorkspaceTabs.Calendar
                // The calendar counts distinct days, not jobs.
                ? inCategory.Where(job => job.Date.Length > 0).Select(job => job.Date)
                    .Distinct(StringComparer.Ordinal).Count()
                : inCategory.Count(job => WorkspaceTabs.Matches(tab, job, query.OpId, today));
        }

        var matching = inCategory
            .Where(job => WorkspaceTabs.Matches(query.Tab, job, query.OpId, today))
            .Where(job => MatchesAssignee(job, query))
            .Where(job => Is(job.Customer, query.Customer))
            .Where(job => Is(job.Trucker, query.Trucker))
            .Where(job => Is(job.Type, query.Type))
            .Where(job => Is(job.Status, query.Status))
            .Where(job => MatchesKpi(job, query))
            .Where(job => MatchesSearch(job, query.Search))
            .ToList();

        var sorted = Sort(matching, query.SortKey, query.SortDir);

        var per = Math.Clamp(query.Per, 1, 200);
        var pageCount = Math.Max(1, (int)Math.Ceiling(sorted.Count / (double)per));
        var page = Math.Clamp(query.Page, 1, pageCount);

        // `Clone` matters: a JsonElement is a window into the JsonDocument it
        // came from, and that document is disposed the moment this method
        // returns. Handing the raw elements back left the endpoint serialising
        // a closed document — which failed as ObjectDisposedException at the
        // point of writing the response, long after anything useful was in the
        // stack trace.
        var rows = sorted.Skip((page - 1) * per).Take(per)
            .Select(job => job.Raw.Clone()).ToList();

        // The calendar strip needs every date in the selection, not just the
        // ones on this page.
        var dates = sorted.Select(job => job.Date).Where(date => date.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(Formats.DateNumber).ToList();

        return new Page(rows, sorted.Count, pageCount, page, counts, dates, updatedAt);
    }

    /// <summary>An exact-value filter, where ALL or empty means no filter.</summary>
    private static bool Is(string value, string wanted) =>
        wanted.Length == 0 || wanted == "ALL"
        || string.Equals(value, wanted, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whose work to show. MY JOBS is already one person's, so the picker does
    /// not narrow it further — matching what the screen does rather than
    /// producing an empty grid when both are set.
    /// </summary>
    private static bool MatchesAssignee(WorkspaceTabs.JobView job, Query query)
    {
        if (query.Tab == WorkspaceTabs.MyJobs) return true;
        if (query.Assignee.Length == 0 || query.Assignee == "ALL") return true;

        return query.Assignee == "My Work"
            ? query.OpId.Length > 0 && string.Equals(job.OwnerId, query.OpId, StringComparison.OrdinalIgnoreCase)
            : string.Equals(job.Owner, query.Assignee, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesKpi(WorkspaceTabs.JobView job, Query query) => query.Kpi switch
    {
        "" or "ALL" => true,
        "Mine" => query.OpId.Length > 0 && string.Equals(job.OwnerId, query.OpId, StringComparison.OrdinalIgnoreCase),
        "Imp" => string.Equals(job.Cat, "IMPORT", StringComparison.OrdinalIgnoreCase),
        "Exp" => string.Equals(job.Cat, "EXPORT", StringComparison.OrdinalIgnoreCase),
        "Del" => string.Equals(job.Cat, "DELIVERY", StringComparison.OrdinalIgnoreCase),
        "Wait" => JobRules.IsWaiting(job.Status),
        "Conf" => JobRules.IsConfirmed(job.Status),
        "Run" => JobRules.IsRunning(job.Status),
        "Delay" => JobRules.IsDelayed(job.Status),
        "Done" => JobRules.IsDone(job.Status),

        // Act and Fmt read validation this side has not been shown to agree on.
        // Answering them with something close would be worse than not answering:
        // the screen would show a plausible number nobody could reconcile.
        _ => true,
    };

    private static bool MatchesCategory(WorkspaceTabs.JobView job, string cat) =>
        cat.Length == 0 || cat == "ALL"
        || string.Equals(job.Cat, cat, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The period bar, on the same <c>dd/MM/yyyy</c> text the register stores.
    /// A job with no date is out as soon as any part of the period is chosen —
    /// it cannot be shown to fall inside a month nobody can place it in.
    /// </summary>
    private static bool MatchesPeriod(WorkspaceTabs.JobView job, Query query)
    {
        var wantsPeriod = Chosen(query.Year) || Chosen(query.Month) || Chosen(query.Day);
        if (!wantsPeriod) return true;

        var parts = job.Date.Split('/');
        if (parts.Length != 3) return false;

        if (Chosen(query.Day)) return job.Date == query.Day;
        if (Chosen(query.Month) && parts[1] != query.Month) return false;
        if (Chosen(query.Year) && parts[2] != query.Year) return false;
        return true;

        static bool Chosen(string value) => value.Length > 0 && value != "ALL";
    }

    private static bool MatchesSearch(WorkspaceTabs.JobView job, string search)
    {
        var q = search.Trim();
        if (q.Length == 0) return true;

        // The same twelve the workspace searches. A narrower list here would
        // quietly stop finding jobs by seal or by driver, which is most of what
        // the box is used for.
        return Has(job.JobCode) || Has(job.Abs) || Has(job.Booking) || Has(job.Customer)
               || Has(job.Container) || Has(job.Seal) || Has(job.Licence) || Has(job.Driver)
               || Has(job.Trucker) || Has(job.Destination) || Has(job.Owner) || Has(job.Sid);

        bool Has(string value) => value.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sorting, with dates ordered as dates.
    ///
    /// <c>dd/MM/yyyy</c> sorted as text puts the second of January before the
    /// first of February of the previous year, which is how a plan ends up
    /// looking shuffled to the person who keyed it.
    /// </summary>
    private static List<WorkspaceTabs.JobView> Sort(
        List<WorkspaceTabs.JobView> rows, string key, string dir)
    {
        var descending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

        Func<WorkspaceTabs.JobView, IComparable> pick = key switch
        {
            "Customer" => job => job.Customer,
            "Trucker" => job => job.Trucker,
            "Status" => job => job.Status,
            "Container" => job => job.Container,
            "Job Code" or "JobCode" => job => job.JobCode,
            "Category" => job => job.Cat,
            _ => job => Formats.DateNumber(job.Date),
        };

        var ordered = descending
            ? rows.OrderByDescending(pick).ThenBy(job => job.Key, StringComparer.Ordinal)
            : rows.OrderBy(pick).ThenBy(job => job.Key, StringComparer.Ordinal);

        return ordered.ToList();
    }
}
