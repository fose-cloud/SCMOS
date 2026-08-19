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
    public record Query(
        string Tab, string Cat, string Year, string Month, string Day,
        string Search, string SortKey, string SortDir, int Page, int Per, string OpId);

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

        return Has(job.Key) || Has(job.JobCode) || Has(job.Container) || Has(job.Customer)
               || Has(job.Trucker) || Has(job.Status) || Has(job.Date);

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
