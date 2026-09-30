using System.Globalization;

namespace Scmos.Api.Rules;

/// <param name="Average">The mean of the days held, to two places; null when no day is held.</param>
/// <param name="Closed">Every day of the month is accounted for, so the average can no longer move.</param>
public record DieselAverage(string Month, decimal? Average, int Days, bool Closed);

/// <summary>
/// The diesel rate a month's work is priced at: the average of that month's daily pump prices, worked
/// out from the published changes. The rule is <c>app/scmos/dieselMonth.ts</c>'s — <c>expand</c> and
/// <c>averageFor</c> — written here for the server's own pricing of a job (30 Sep 2026: a Billing Case
/// shows the job's contract rate). The two are held to one answer by <c>tests/fixtures/billing-parity.json</c>,
/// which both sides' checks read: a rule written twice has always drifted in this repository.
///
/// <para>
/// The arithmetic is the web's, double for double: the prices are summed in day order, divided, and
/// rounded half up to two places (JavaScript's <c>Math.round</c>), so a mean that lands on a half cent
/// lands the same way on both sides.
/// </para>
/// </summary>
public static class DieselMonth
{
    /// <summary>The month a dd/MM/yyyy date belongs to, as MM/yyyy; empty when unreadable.</summary>
    public static string MonthOf(string? date)
    {
        var text = (date ?? "").Trim();
        return Readable(text) ? text[3..] : "";
    }

    /// <summary>How many days a MM/yyyy month has; 0 when unreadable.</summary>
    public static int DaysInMonth(string month)
    {
        if (month.Length != 7 || month[2] != '/' || !int.TryParse(month[..2], out var m) || !int.TryParse(month[3..], out var y)) return 0;
        return m is < 1 or > 12 || y is < 1 or > 9999 ? 0 : DateTime.DaysInMonth(y, m);
    }

    /// <summary>
    /// A month's daily prices from the changes around it: the price in force on the 1st is the last change
    /// on or before it; each change holds until the next. A day after <paramref name="until"/> has no price
    /// yet, and a month before any recorded change is empty rather than guessed.
    /// </summary>
    public static IReadOnlyList<(string Date, double Price)> Expand(IEnumerable<(string Date, double Price)> changes,
        string month, string? until = null)
    {
        var total = DaysInMonth(month);
        if (total == 0) return [];
        var stop = Sortable(until ?? "");
        var usable = changes.Where(one => Sortable(one.Date).Length > 0 && double.IsFinite(one.Price) && one.Price > 0)
            .OrderBy(one => Sortable(one.Date), StringComparer.Ordinal).ToList();
        if (usable.Count == 0) return [];

        double? held = null;
        var first = Sortable("01/" + month);
        foreach (var one in usable)
            if (string.CompareOrdinal(Sortable(one.Date), first) <= 0) held = one.Price;

        var days = new List<(string, double)>();
        for (var day = 1; day <= total; day++)
        {
            var date = day.ToString("00", CultureInfo.InvariantCulture) + "/" + month;
            if (stop.Length > 0 && string.CompareOrdinal(Sortable(date), stop) > 0) break;
            var changed = usable.FirstOrDefault(one => one.Date == date);
            if (changed.Date is not null) held = changed.Price;
            if (held is { } price) days.Add((date, price));
        }
        return days;
    }

    /// <summary>The month's average off the days held for it — see the class notes for the rounding.</summary>
    public static DieselAverage AverageFor(IEnumerable<(string Date, double Price)> days, string month)
    {
        var mine = days.Where(day => MonthOf(day.Date) == month && double.IsFinite(day.Price) && day.Price > 0).ToList();
        var total = DaysInMonth(month);
        var closed = total > 0 && mine.Count >= total;
        if (mine.Count == 0) return new(month, null, 0, false);
        var sum = 0d;
        foreach (var day in mine) sum += day.Price;
        var mean = sum / mine.Count;
        var rounded = Math.Floor(mean * 100 + 0.5) / 100;
        return new(month, decimal.Round((decimal)rounded, 2), mine.Count, closed);
    }

    /// <summary>The rate a job is priced at — its month's average up to <paramref name="until"/> — or null when that month has no price.</summary>
    public static DieselAverage? ForJob(IEnumerable<(string Date, double Price)> changes, string? jobDate, string? until)
    {
        var month = MonthOf(jobDate);
        if (month.Length == 0) return null;
        var found = AverageFor(Expand(changes, month, until), month);
        return found.Average is null ? null : found;
    }

    private static bool Readable(string text) => text.Length == 10 && text[2] == '/' && text[5] == '/'
        && text.Where((ch, i) => i is not (2 or 5)).All(char.IsAsciiDigit);

    private static string Sortable(string date)
    {
        var text = (date ?? "").Trim();
        return Readable(text) ? text[6..] + text[3..5] + text[..2] : "";
    }
}
