namespace Scmos.Api.Rules;

/// <summary>
/// One price, spread across the diesel bands.
///
/// A transport rate is never one number. The contract's fuel clause steps it up
/// each time diesel crosses into the next band, so a lane is quoted once — at
/// the lowest band — and the rest of the row follows from it. That is how every
/// carrier fills in the LESCHACO form, and it is what lets the rate sheet ask
/// for a single figure per vehicle instead of seven.
///
/// <h3>The step, measured rather than assumed</h3>
///
/// Taken over every consecutive pair of prices in the register on 3 September
/// 2026 — 70,516 of them, across eighteen carriers:
///
/// <list type="bullet">
///   <item>70,393 (99.8%) are <c>ceiling(previous × 1.03)</c></item>
///   <item>83 (0.1%) round down instead</item>
///   <item>20 floor, 6 repeat the price, 14 are something else entirely</item>
/// </list>
///
/// So the step is three percent <b>rounded up</b>, and it compounds on the
/// rounded figure rather than on the original — 5,000 gives 5,150 then 5,305
/// (from 5,304.50) then 5,465 (from 5,464.15), which is the row the register
/// actually holds. Excel's ROUNDUP, not ROUND: plain rounding reproduces 83 of
/// those 70,516 pairs and breaks the other 70,393.
///
/// <h3>Why decimal and not double</h3>
///
/// <c>5000 * 1.03</c> in binary floating point is 5150.0000000000009, and the
/// ceiling of that is 5151. Every price in the book would be a baht high. The
/// arithmetic is decimal for that reason and must stay decimal.
/// </summary>
public static class FuelLadder
{
    /// <summary>The fuel clause's step. Three percent, per band crossed.</summary>
    public const decimal Step = 1.03m;

    /// <summary>
    /// The band ladder the quotation is built on, lowest first.
    ///
    /// Seven rungs, and it is not a choice — 11,679 of the 11,746 priced rows
    /// in the register use exactly this set, and the next most common shape has
    /// thirty-six. The others are older forms from carriers who have not been
    /// re-quoted; they are read fine, they are simply not what a new quotation
    /// is built on.
    ///
    /// Matched to the band table by the top of the range rather than by the
    /// label, because the same rung is spelled two ways: the rate book writes
    /// "≤ 29.99" and the inquiry workbook writes "00.01–29.99".
    /// </summary>
    public static readonly decimal[] Rungs =
        [29.99m, 32.99m, 36.29m, 39.91m, 43.94m, 48.34m, 53.18m];

    /// <summary>
    /// The next band's price: three percent up, rounded up to the baht.
    /// </summary>
    public static int Next(int price) => (int)Math.Ceiling(price * Step);

    /// <summary>
    /// Which rung a quoted band sits on, or -1 when it is not on the ladder.
    ///
    /// The inquiry carries its band as text — "00.01–29.99", "30.00–32.99" —
    /// and the number that identifies it is the top of the range. A quotation
    /// is not always taken at the bottom rung: of 3,005 lanes in the register,
    /// 1,806 were quoted at 30.00–32.99 and 1,199 at 00.01–29.99. Starting all
    /// of them at the bottom would put a 3% step under three fifths of the
    /// book.
    /// </summary>
    public static int RungOf(string? band)
    {
        var top = TopOf(band);
        if (top is null) return -1;
        for (var at = 0; at < Rungs.Length; at++)
        {
            // To the satang, because these are money ranges and 36.29 and 36.30
            // are different rungs.
            if (Math.Abs(Rungs[at] - top.Value) < 0.005m) return at;
        }
        return -1;
    }

    /// <summary>
    /// The top of a band's range — "30.00–32.99" is 32.99, "≤ 29.99" is 29.99.
    /// Null when the text is not a band at all.
    /// </summary>
    public static decimal? TopOf(string? band)
    {
        var text = (band ?? "").Trim();
        if (text.Length == 0) return null;

        // Any of the dashes the two files use, and the "≤ n" form the rate book
        // writes for its open-bottomed first band.
        var parts = text.Split(['-', '–', '—'], StringSplitOptions.TrimEntries);
        var last = parts[^1].TrimStart('≤', '<', '=', ' ');
        return decimal.TryParse(last, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>
    /// Where each rung sits in a band table, or -1 for a rung that table lacks.
    ///
    /// The ladder is seven rungs; the band table is a union of every shape the
    /// eighteen carriers' forms use, and this ladder's rungs sit at positions
    /// 0, 2, 5, 7, 9, 13 and 17 of it. Writing a rung's price at its own index
    /// instead put the second rung's figure under "27.00-29.99" — a real band
    /// belonging to a different carrier's form — so the row read as though a
    /// price had been quoted at a diesel range nobody quoted it at.
    ///
    /// Matched on the top of the range. Two bands share a top at 29.99 —
    /// "≤ 29.99" and "27.00-29.99" — and the first is taken, because that is
    /// the one the 11,679-row ladder is written against.
    /// </summary>
    public static int[] PositionsIn(IReadOnlyList<(decimal Max, int Position)> bands)
    {
        var at = new int[Rungs.Length];
        for (var rung = 0; rung < Rungs.Length; rung++)
        {
            at[rung] = -1;
            foreach (var band in bands)
            {
                if (Math.Abs(band.Max - Rungs[rung]) >= 0.005m) continue;
                at[rung] = band.Position;
                break;
            }
        }
        return at;
    }

    /// <summary>
    /// A quoted price spread up the ladder from the rung it was quoted on.
    ///
    /// <paramref name="positions"/> comes from <see cref="PositionsIn"/> and
    /// says which column of the band table each rung is; without it the prices
    /// land under whichever bands happen to be first in the table.
    ///
    /// Rungs below the quoted one are left empty rather than worked backwards.
    /// Dividing by 1.03 does not undo rounding up — 5,150 comes back as 5,000
    /// but 5,305 comes back as 5,150.49 — so a reversed ladder would print
    /// prices nobody agreed to, one satang at a time, under a heading that says
    /// they are contracted.
    /// </summary>
    public static int?[] Expand(int price, int fromRung, int[] positions, int width)
    {
        var row = new int?[width];
        if (price <= 0 || fromRung < 0 || fromRung >= Rungs.Length) return row;

        var value = price;
        for (var rung = fromRung; rung < Rungs.Length; rung++)
        {
            var column = rung < positions.Length ? positions[rung] : -1;
            // A rung the band table does not carry still steps the price — the
            // fuel clause does not stop because a column is missing — it simply
            // has nowhere to be shown.
            if (column >= 0 && column < width) row[column] = value;
            value = Next(value);
        }
        return row;
    }
}
