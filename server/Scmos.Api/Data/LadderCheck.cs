using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// Runs the fuel clause against rows the register actually holds, with
/// <c>--check-ladder</c>.
///
/// The rows below are copied out of the rate book, not invented: 9ISARA's
/// LOTUS ASIA to LCB PORT container rate and its reefer rate, both quoted at
/// the bottom band and stepped up by the fuel clause. If the arithmetic here
/// stops reproducing them, the screen has started printing prices no carrier
/// agreed to.
/// </summary>
public static class LadderCheck
{
    private static readonly (string Why, int Base, int[] Row)[] Rows =
    [
        ("9ISARA · 20F and 40F, LOTUS ASIA to LCB PORT",
            5000, [5000, 5150, 5305, 5465, 5629, 5798, 5972]),
        ("9ISARA · the reefer rate on the same lane",
            8700, [8700, 8961, 9230, 9507, 9793, 10087, 10390]),
    ];

    private static readonly (string Why, int From, int Want)[] Steps =
    [
        // Rounded up, and compounding on the rounded figure. Plain rounding
        // gives 5464 here and every rung above it drifts further.
        ("a half-baht steps up, not down", 5150, 5305),
        ("and the next one compounds on that", 5305, 5465),
        ("an exact multiple gains nothing extra", 5000, 5150),
        // The one that makes this decimal arithmetic: 5000 * 1.03 in binary
        // floating point is 5150.0000000000009, whose ceiling is 5151.
        ("a price that lands exactly is not pushed up by float error", 10000, 10300),
        ("nor is this one", 20000, 20600),
        ("a single baht still steps", 1, 2),
    ];

    private static readonly (string Why, string Band, int Rung)[] Bands =
    [
        ("the rate book's spelling of the bottom rung", "≤ 29.99", 0),
        ("the inquiry workbook's spelling of the same rung", "00.01–29.99", 0),
        ("a plain hyphen, which one of the files uses", "00.01-29.99", 0),
        ("the second rung, where most of the register is quoted", "30.00–32.99", 1),
        ("the third", "33.00–36.29", 2),
        ("the top rung", "48.35–53.18", 6),
        ("a band from an older form is not on this ladder", "33.00–35.99", -1),
        ("nor is one a satang away", "36.00–38.99", -1),
        ("blank is not a band", "", -1),
        ("and neither is prose", "based on diesel", -1),
    ];

    /// <summary>
    /// The band table as the register holds it — every shape the eighteen
    /// carriers' forms use, merged. The ladder's seven rungs are scattered
    /// through it at 0, 2, 5, 7, 9, 13 and 17, which is the whole reason
    /// <see cref="FuelLadder.PositionsIn"/> exists.
    /// </summary>
    private static readonly (decimal Max, int Position)[] Table =
    [
        (29.99m, 0), (29.99m, 1), (32.99m, 2), (32.99m, 3), (35.99m, 4), (36.29m, 5),
        (38.99m, 6), (39.91m, 7), (41.99m, 8), (43.94m, 9), (45.00m, 10), (45.99m, 11),
        (48.00m, 12), (48.34m, 13), (48.99m, 14), (51.00m, 15), (51.99m, 16), (53.18m, 17),
        (53.99m, 18), (54.99m, 19), (56.99m, 20), (57.99m, 21), (59.99m, 22), (60.99m, 23),
    ];

    private const int Width = 24;

    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-ladder")) return null;

        var failed = 0;
        var where = FuelLadder.PositionsIn(Table);

        Console.WriteLine("Which column of the band table each rung is.");
        Console.WriteLine();
        var wantColumns = new[] { 0, 2, 5, 7, 9, 13, 17 };
        var columnsRight = where.SequenceEqual(wantColumns);
        if (!columnsRight) failed++;
        Console.WriteLine($"  {(columnsRight ? "ok  " : "FAIL")}  the seven rungs land on 0 2 5 7 9 13 17");
        if (!columnsRight) Console.WriteLine($"          got {string.Join(" ", where)}");
        Console.WriteLine();

        Console.WriteLine("One quoted price, spread up the diesel bands.");
        Console.WriteLine();
        foreach (var (why, basePrice, want) in Rows)
        {
            var full = FuelLadder.Expand(basePrice, 0, where, Width);
            // Read back through the rungs' own columns, which is how the screen
            // reads it: the prices are scattered across the union table, and
            // squeezing them back together is what proves they went to the
            // right places rather than merely to seven places.
            var got = where.Select(column => full[column]).ToArray();
            var same = got.Length == want.Length
                && !got.Where((value, at) => value != want[at]).Any();
            if (!same) failed++;
            Console.WriteLine($"  {(same ? "ok  " : "FAIL")}  {why}");
            if (!same)
            {
                Console.WriteLine($"          wanted  {string.Join(" ", want)}");
                Console.WriteLine($"          got     {string.Join(" ", got.Select(v => v?.ToString() ?? "—"))}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("The step itself.");
        Console.WriteLine();
        foreach (var (why, from, want) in Steps)
        {
            var got = FuelLadder.Next(from);
            var ok = got == want;
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
            if (!ok) Console.WriteLine($"          {from} -> wanted {want}, got {got}");
        }

        Console.WriteLine();
        Console.WriteLine("Which rung a written band is.");
        Console.WriteLine();
        foreach (var (why, band, want) in Bands)
        {
            var got = FuelLadder.RungOf(band);
            var ok = got == want;
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
            if (!ok) Console.WriteLine($"          \"{band}\" -> wanted {want}, got {got}");
        }

        Console.WriteLine();
        Console.WriteLine("Where a quotation taken above the bottom band starts.");
        Console.WriteLine();
        // Three fifths of the register is quoted at 30.00–32.99. Started at the
        // bottom rung it would carry an extra 3% all the way up, and the rung
        // below it would show a price that was never quoted.
        var second = FuelLadder.Expand(5000, 1, where, Width);
        var startsEmpty = second[where[0]] is null;
        var sitsRight = second[where[1]] == 5000 && second[where[2]] == 5150;
        foreach (var (why, ok) in new[]
                 {
                     ("the rung below the quote is left empty, not worked backwards", startsEmpty),
                     ("the quoted price sits on the rung it was quoted at", sitsRight),
                 })
        {
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
        }

        var total = Rows.Length + Steps.Length + Bands.Length + 3;
        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? $"All {total} cases hold."
            : $"{failed} of {total} cases failed.");
        return failed == 0 ? 0 : 1;
    }
}
