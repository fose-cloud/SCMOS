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
public class WorkspaceService(JobRegisterCache register, CarrierDirectory carriers)
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
        /// <summary>A span of plan dates, dd/MM/yyyy, either end optional.</summary>
        string From, string To,
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
        DateTimeOffset UpdatedAt,
        /// <summary>
        /// Who appears in this tab before the customer and haulier filters are
        /// applied — what those two dropdowns offer.
        ///
        /// Sent from here because the browser holds one page when the API is
        /// paging, and a list built from one filtered page cannot offer the
        /// second name somebody is trying to add.
        /// </summary>
        IReadOnlyList<string> Customers,
        IReadOnlyList<string> Truckers);

    public async Task<Page> ReadAsync(Query query, CancellationToken token)
    {
        var snapshot = await register.ReadAsync(token);
        var directory = await carriers.ReadAsync(token);
        var updatedAt = snapshot.UpdatedAt;

        // One "today" for the whole answer. Reading the clock per job would let
        // a request that straddles midnight count a job on TODAY and then leave
        // it off the page.
        var today = DateOnly.FromDateTime(DateTime.Now);

        // The category narrows everything, including the tab counts — the strip
        // shows what is on that tab *within* the category you are looking at,
        // which is what the browser did.
        var inCategory = new List<WorkspaceTabs.JobView>();
        foreach (var row in snapshot.Rows)
        {
            var job = WorkspaceTabs.JobView.From(row.Raw);
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

        // Everything except the two filters that may name several values.
        // The lists those two choose from are taken from here, so picking one
        // customer cannot remove the rest from the dropdown — which is what
        // happened while the options were read off the rows in the browser and
        // the API was doing the paging.
        var beforeNames = inCategory
            .Where(job => WorkspaceTabs.Matches(query.Tab, job, query.OpId, today))
            .Where(job => MatchesAssignee(job, query))
            .Where(job => Is(job.Type, query.Type))
            .Where(job => Is(job.Status, query.Status))
            .Where(job => MatchesKpi(job, query))
            .Where(job => MatchesSearch(job, query.Search))
            .ToList();

        var matching = beforeNames
            .Where(job => IsAny(job.Customer, query.Customer))
            // Through the register: choosing "Sangja Transport Co., Ltd."
            // finds the jobs written SJ and SANGJA as well, which is the whole
            // reason the spellings were reconciled.
            .Where(job => Wanted(query.Trucker).Length == 0
                || Wanted(query.Trucker).Any(one => directory.Same(job.Trucker, one)))
            .ToList();

        // Commonest first: the dropdown is read from the top, and a list of a
        // hundred customers alphabetically buries the five anybody wants.
        var customers = beforeNames
            .Select(job => job.Customer.Trim()).Where(name => name.Length > 0)
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.First()).ToList();

        var truckers = beforeNames
            .Select(job => directory.Company(job.Trucker)).Where(name => name.Length > 0)
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.First()).ToList();

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

        return new Page(rows, sorted.Count, pageCount, page, counts, dates, updatedAt,
            customers, truckers);
    }

    /// <summary>
    /// The values the screen sends when a filter is not set.
    ///
    /// Three different words for the same idea, because each picker was written
    /// with the label that reads best above it — "ALL" for a category, "All
    /// Team" for the assignee, "All" for the KPI drill. Listing them here is
    /// less pleasant than one sentinel and much better than a server that reads
    /// "All Team" as somebody's name and answers with an empty grid.
    /// </summary>
    private static bool NotSet(string value) =>
        value.Length == 0
        || value.Equals("ALL", StringComparison.OrdinalIgnoreCase)
        || value.Equals("All Team", StringComparison.OrdinalIgnoreCase);

    /// <summary>An exact-value filter, where an unset value means no filter.</summary>
    private static bool Is(string value, string wanted) =>
        NotSet(wanted) || string.Equals(value, wanted, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The values a filter is asking for, when it may ask for several.
    ///
    /// The screen sends them pipe-separated in the same parameter it always
    /// used, so nothing about the request shape changes and an older caller
    /// sending one name still works. A pipe rather than a comma because a
    /// company name may contain a comma and none of them contain a pipe.
    /// </summary>
    private static string[] Wanted(string value) =>
        NotSet(value)
            ? []
            : value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>An any-of filter, where an unset value means no filter.</summary>
    private static bool IsAny(string value, string wanted)
    {
        var list = Wanted(wanted);
        return list.Length == 0
            || list.Any(one => string.Equals(value, one, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whose work to show. MY JOBS is already one person's, so the picker does
    /// not narrow it further — matching what the screen does rather than
    /// producing an empty grid when both are set.
    /// </summary>
    private static bool MatchesAssignee(WorkspaceTabs.JobView job, Query query)
    {
        if (query.Tab == WorkspaceTabs.MyJobs) return true;
        if (NotSet(query.Assignee)) return true;

        return query.Assignee == "My Work"
            ? query.OpId.Length > 0 && string.Equals(job.OwnerId, query.OpId, StringComparison.OrdinalIgnoreCase)
            : string.Equals(job.Owner, query.Assignee, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesKpi(WorkspaceTabs.JobView job, Query query) => query.Kpi switch
    {
        "" or "ALL" or "All" => true,
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
        // The span first: it is the only one of these that can be half-open,
        // and it is set from a box somebody typed a date into rather than from
        // a picker, so it answers "the first week of August" — a question none
        // of the three below can be asked.
        var from = Formats.DateNumber(query.From);
        var to = Formats.DateNumber(query.To);
        if (from > 0 || to > 0)
        {
            var day = Formats.DateNumber(job.Date);
            if (day == 0) return false;
            if (from > 0 && day < from) return false;
            if (to > 0 && day > to) return false;
        }

        var wantsPeriod = Chosen(query.Year) || Chosen(query.Month) || Chosen(query.Day);
        // Past the span and nothing else chosen: the job is in.
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

        // Keyed on the labels the grid's headers actually carry, because that is
        // what it sends. It sent "Truck" and this expected "Trucker", so the one
        // carrier column anybody sorts by fell through to the default and came
        // back in plan order — the header drew its arrow and nothing moved.
        Func<WorkspaceTabs.JobView, IComparable> pick = key switch
        {
            "Customer" => job => job.Customer,
            "Truck" or "Trucker" => job => job.Trucker,
            "Status" => job => job.Status,
            "No Container" or "Container" => job => job.Container,
            "No Seal" or "Seal" => job => job.Seal,
            "Job Code" or "JobCode" => job => job.JobCode,
            "Job / ABS" => job => job.JobCode.Length > 0 ? job.JobCode : job.Abs,
            "ABS No." => job => job.Abs,
            "Booking" => job => job.Booking,
            "SID No." or "SID NO." => job => job.Sid,
            // The Domestic grid heads its columns the way the account's own
            // sheet heads them, and sends those labels. Without these it sorted
            // by carrier and drew an arrow over a column that had not moved.
            "TRUCK" => job => job.Trucker,
            "SID NUMBER" => job => job.JobCode,
            "Customer List" => job => job.Customer,
            "Pick-Up Date" => job => Formats.DateNumber(job.Date),
            "Category" => job => job.Cat,
            "Type" => job => job.Type,
            "Destination" => job => job.Destination,
            "Licence" => job => job.Licence,
            "Driver" or "Driver Name" => job => job.Driver,
            "Reason / Delay" => job => job.Reason,
            "Assigned To" => job => job.Owner,
            "Own" => job => job.OwnerId,
            "Date" or "Plan Loading Date" => job => Formats.DateNumber(job.Date),

            // Anything else is a column this view has no field for — the weight,
            // the arrival, the pickup plan. It comes back in plan order, and the
            // browser re-sorts on the whole register the moment that arrives,
            // which is where every column can be sorted properly.
            _ => job => Formats.DateNumber(job.Date),
        };

        // Carrier before key as the tie-break. Jobs on the same day are worked
        // carrier by carrier — one call arranges four trucks — so a day that
        // sorts by job key scatters that conversation across the page. The key
        // stays last so the order is still total and a page boundary cannot
        // shuffle two rows between requests.
        var ordered = descending
            ? rows.OrderByDescending(pick)
                .ThenBy(job => job.Trucker, StringComparer.OrdinalIgnoreCase)
                .ThenBy(job => job.Key, StringComparer.Ordinal)
            : rows.OrderBy(pick)
                .ThenBy(job => job.Trucker, StringComparer.OrdinalIgnoreCase)
                .ThenBy(job => job.Key, StringComparer.Ordinal);

        return ordered.ToList();
    }
}
