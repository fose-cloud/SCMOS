using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// Runs the filter bar's own rules, with <c>--check-filter</c>.
///
/// These were the workspace's, written inside its service, and the rate sheet
/// now reads the same bar. Two screens on one rule is only worth anything if
/// the rule is checked, and the cases below are the ones that were actually got
/// wrong: what "no date" means beside a real year, and what happens to a filter
/// on a column that holds a list.
///
/// The Subcon column is the second of those. It holds "SANGJA,SSL,PHURADA" —
/// 332 of the first 400 rows of the register name more than one carrier — so
/// equality matches almost nothing, and a plain Contains lets a short name
/// match a longer one it has nothing to do with.
/// </summary>
public static class FilterCheck
{
    private static readonly (string Why, string Date, string Year, string Month, string Day, bool In)[] Periods =
    [
        ("an untouched bar keeps everything", "09/01/2026", "", "", "", true),
        ("and keeps a row it cannot date", "", "", "", "", true),
        ("a year matches its own rows", "09/01/2026", "2026", "", "", true),
        ("and excludes another year's", "09/01/2025", "2026", "", "", false),
        ("several years is any of them", "09/01/2025", "2025|2026", "", "", true),
        ("a month is the middle field, not the first", "09/01/2026", "", "01", "", true),
        ("so the ninth of January is not September", "09/01/2026", "", "09", "", false),
        ("a day carries its whole date", "09/01/2026", "", "", "09/01/2026", true),
        ("which keeps two months' ninths apart", "09/02/2026", "", "", "09/01/2026", false),

        // The one this bar existed to fix. An undated row is invisible under
        // every other choice, and it is the row somebody has to go and correct.
        ("choosing a year hides a row with no date", "", "2026", "", "", false),
        ("but no-date is a choice of its own", "", "NONE", "", "", true),
        ("beside a real year, it still shows", "", "2026|NONE", "", "", true),
        ("and that year's rows still show beside it", "09/01/2026", "2026|NONE", "", "", true),
        ("a dated row does not answer to no-date alone", "09/01/2026", "NONE", "", "", false),
        ("a stale month never hides an undated row", "", "NONE", "07", "", true),
        ("nor does a stale day", "", "NONE", "", "09/01/2026", true),

        ("a date that is not one is undated", "31 Aug 2026", "2026", "", "", false),
        ("and so is a half-written one", "01/2026", "2026", "", "", false),
    ];

    private static readonly (string Why, string Field, string Wanted, bool Match)[] Lists =
    [
        ("an untouched picker keeps every row", "SANGJA,SSL,PHURADA", "", true),
        ("a carrier named first is found", "SANGJA,SSL,PHURADA", "SANGJA", true),
        ("one in the middle just as well", "SANGJA,SSL,PHURADA", "SSL", true),
        ("and one at the end", "SANGJA,SSL,PHURADA", "PHURADA", true),
        ("a carrier not on the row is not found", "SANGJA,SSL", "WEALTHY", false),
        ("any of several is enough", "SANGJA,SSL", "WEALTHY|SSL", true),
        ("spaces around a name are the file's, not the name's", "SANGJA, SSL , PHURADA", "SSL", true),
        ("case is not a different carrier", "sangja,ssl", "SSL", true),
        ("a single-carrier row still works", "SSL", "SSL", true),

        // A substring test would pass every one of these, and each is a
        // different company being quoted for somebody else's journey.
        ("a name inside a longer one is not that carrier", "SSLOGISTICS", "SSL", false),
        ("nor is the longer one inside the shorter", "SSL", "SSLOGISTICS", false),
        ("and not across the separator either", "SANG,JA", "SANGJA", false),
        ("an empty column matches nothing asked for", "", "SSL", false),
    ];

    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-filter")) return null;

        var failed = 0;

        Console.WriteLine("When a date falls inside the chosen period.");
        Console.WriteLine();
        foreach (var (why, date, year, month, day, want) in Periods)
        {
            var got = AnyOfFilter.InPeriod(date, year, month, day);
            var ok = got == want;
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
            if (!ok)
            {
                var shown = date.Length == 0 ? "(blank)" : date;
                Console.WriteLine($"          {shown} against year={year} month={month} day={day}");
                Console.WriteLine($"          wanted {want}, got {got}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("When a column holding a list matches a picker.");
        Console.WriteLine();
        foreach (var (why, field, wanted, want) in Lists)
        {
            var got = AnyOfFilter.IsAnyOfList(field, wanted);
            var ok = got == want;
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
            if (!ok)
            {
                Console.WriteLine($"          \"{field}\" against \"{wanted}\"");
                Console.WriteLine($"          wanted {want}, got {got}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? $"All {Periods.Length + Lists.Length} cases hold."
            : $"{failed} of {Periods.Length + Lists.Length} cases failed.");
        return failed == 0 ? 0 : 1;
    }
}
