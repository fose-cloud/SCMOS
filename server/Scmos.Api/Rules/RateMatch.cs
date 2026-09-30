using System.Text.RegularExpressions;

namespace Scmos.Api.Rules;

/// <summary>
/// How a job finds its price in a carrier's rate book — the three rules Booking already prices a job with
/// (<c>app/scmos/booking.ts</c> <c>vehicleForType</c> and the lane's token overlap, <c>app/scmos/rates.ts</c>
/// <c>priceFor</c>), written here so a Billing Case can show the job's contract rate and the invoice check can
/// test a claim against it (30 Sep 2026). <c>tests/fixtures/billing-parity.json</c> holds this copy to the
/// browser's answers, case by case.
///
/// <para>
/// The regular expressions run in ECMAScript mode so <c>\b</c> and <c>\d</c> mean what they mean in the browser:
/// ASCII word boundaries and ASCII digits. A Thai stop word therefore only drops where the browser's does.
/// </para>
/// </summary>
public static class RateMatch
{
    private const RegexOptions Js = RegexOptions.ECMAScript | RegexOptions.CultureInvariant;

    private static readonly Regex Stop = new(@"\b(co|ltd|company|limited|thailand|th|inc|plc|จำกัด|บริษัท|มหาชน)\b", Js | RegexOptions.IgnoreCase);
    private static readonly Regex NotWord = new(@"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant);
    private static readonly Regex Space = new(@"\s+", Js);
    private static readonly Regex Dg = new(@"\bDG\b", Js);
    private static readonly Regex Wheels = new(@"(\d{1,2})\s*W(?:H|HEELS?)?\b", Js);
    private static readonly Regex Tank = new("TK|TANK|ISO", Js);
    private static readonly Regex Reefer = new(@"REEFER|RF\b", Js);

    /// <summary>The plan's container wording onto the rate cards' vocabulary: 1X40' → 40F, 1x6 WH → 6W, 1X20' TK → ISO TANK.</summary>
    public static string VehicleForType(string? type)
    {
        var value = Space.Replace((type ?? "").ToUpperInvariant(), " ").Trim();
        if (value.Length == 0) return "";
        var dg = Dg.IsMatch(value);
        var wheels = Wheels.Match(value);
        var vehicle = "";
        if (Tank.IsMatch(value)) vehicle = "ISO TANK";
        else if (Reefer.IsMatch(value)) vehicle = value.Contains("40") ? "40RF" : "20RF";
        else if (wheels.Success) vehicle = int.Parse(wheels.Groups[1].Value) + "W";
        else if (value.Contains("40")) vehicle = "40F";
        else if (value.Contains("20")) vehicle = "20F";
        if (vehicle.Length == 0) return "";
        if (vehicle == "ISO TANK") return vehicle;
        return dg ? vehicle + " DG" : vehicle;
    }

    /// <summary>The words a name is matched on: lower case, company words dropped, three letters or more.</summary>
    public static IReadOnlyList<string> Tokens(string? value)
    {
        var text = Stop.Replace((value ?? "").ToLowerInvariant(), " ");
        return NotWord.Replace(text, " ").Split(' ').Where(word => word.Length > 2).ToList();
    }

    /// <summary>Token overlap, 0–1: the share of the shorter list found in the other.</summary>
    public static double Overlap(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var set = new HashSet<string>(b, StringComparer.Ordinal);
        return (double)a.Count(set.Contains) / Math.Min(a.Count, b.Count);
    }

    /// <summary>How well a lane fits a job: the job's customer and destination against the lane's customer and either end.</summary>
    public static double Score(string customer, string destination, string laneCustomer, string laneFrom, string laneTo)
    {
        var wanted = Tokens($"{customer} {destination}");
        return Math.Max(Overlap(wanted, Tokens($"{laneCustomer} {laneTo}")), Overlap(wanted, Tokens($"{laneCustomer} {laneFrom}")));
    }

    /// <summary>The least a lane must fit a job to be its lane — Booking's own bar.</summary>
    public const double Fits = 0.5;

    /// <summary>
    /// A lane's price for one vehicle at a diesel figure: the cheapest band the lane quotes whose ceiling still
    /// covers it, or, past every band it quotes, its top one. <paramref name="row"/> is the lane's price per band,
    /// with each band's ceiling; a band it does not quote is null. Returns the band's index, or -1.
    /// </summary>
    public static int BandIn(IReadOnlyList<(decimal Max, decimal? Price)> row, decimal diesel)
    {
        var best = -1;
        for (var i = 0; i < row.Count; i++)
        {
            if (row[i].Price is null || row[i].Max < diesel) continue;
            if (best < 0 || row[i].Max < row[best].Max) best = i;
        }
        if (best >= 0) return best;
        var top = -1;
        for (var i = 0; i < row.Count; i++)
        {
            if (row[i].Price is null) continue;
            if (top < 0 || row[i].Max > row[top].Max) top = i;
        }
        return top;
    }
}
