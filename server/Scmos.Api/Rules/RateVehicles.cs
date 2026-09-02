namespace Scmos.Api.Rules;

/// <summary>
/// The things a rate can be quoted for.
///
/// Twenty-four of them, taken column for column from the Rate Inquiry workbook
/// the team keeps — the four truck blocks, the container blocks, the tanks and
/// the two special vehicles. The codes are the ones the rate book already
/// speaks (<c>20F</c>, <c>4W DG</c>), so a price recorded against an inquiry and
/// a price sitting in <see cref="Data.RatePrice"/> are talking about the same
/// vehicle rather than two spellings of it.
///
/// This list is <b>served to the browser</b> rather than repeated there. Every
/// rule this project has written twice has drifted, and a form that offers a
/// vehicle the API will not accept is the same bug wearing a different hat.
/// </summary>
public static class RateVehicles
{
    /// <summary>Which block of the sheet a vehicle sits in — what the form groups by.</summary>
    public const string Truck = "TRUCK";
    public const string Container = "CONTAINER";
    public const string Tank = "TANK";
    public const string Special = "SPECIAL";

    /// <param name="Code">What a price is stored against.</param>
    /// <param name="Label">The workbook's own heading for the column.</param>
    /// <param name="Group">Truck, container, tank or special.</param>
    /// <param name="Dg">Dangerous goods.</param>
    /// <param name="Reefer">Temperature controlled.</param>
    public readonly record struct Vehicle(string Code, string Label, string Group, bool Dg, bool Reefer);

    public static readonly Vehicle[] All =
    [
        // LCL — a truck, priced by wheels, then by what it is carrying.
        new("4W", "4W", Truck, false, false),
        new("6W", "6W", Truck, false, false),
        new("10W", "10W", Truck, false, false),
        new("4W DG", "4W DG", Truck, true, false),
        new("6W DG", "6W DG", Truck, true, false),
        new("10W DG", "10W DG", Truck, true, false),
        new("4W RF", "4W Reefer", Truck, false, true),
        new("6W RF", "6W Reefer", Truck, false, true),
        new("10W RF", "10W Reefer", Truck, false, true),
        new("4W RF DG", "4W Reefer DG", Truck, true, true),
        new("6W RF DG", "6W Reefer DG", Truck, true, true),
        new("10W RF DG", "10W Reefer DG", Truck, true, true),

        // FCL — a box on a trailer.
        new("20F", "20'", Container, false, false),
        new("40F", "40' / 40'HQ", Container, false, false),
        new("20F DG", "20' DG", Container, true, false),
        new("40F DG", "40' / 40'HQ DG", Container, true, false),
        new("20RF", "20' Reefer", Container, false, true),
        new("40RF", "40' / 40'HQ Reefer", Container, false, true),
        new("20OT", "20' OT (IG)", Container, false, false),

        new("20TK", "ISO Tank", Tank, false, false),
        new("20TK DG", "20' ISO Tank DG", Tank, true, false),
        new("40TK", "40' ISO Tank", Tank, false, false),

        new("6W FB", "6WH Flatbed", Special, false, false),
        new("10W HIAB", "10WH Hiab Truck", Special, false, false),

        // Priced in the inquiry workbook and missing here until 2026-09-02, so
        // nine quotes across fourteen months had nowhere to land. Few, and that
        // is the point: a vehicle asked for twice a year is exactly the one
        // nobody remembers to add, and the price was being typed into a column
        // this system could not read.
        new("6W HIAB", "6WH Hiab Truck", Special, false, false),
        new("SIDE", "Side Curtain Truck", Special, false, false),
        new("FBT", "Flat-bed Trailer", Special, false, false),
        new("FBT DG", "Flat-bed Trailer DG", Special, true, false),
    ];

    private static readonly HashSet<string> Codes =
        new(All.Select(vehicle => vehicle.Code), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A price against a vehicle nobody quotes for is a typo or a stale form,
    /// and either way it would sit in the rate history unanswerable. Refused
    /// rather than stored.
    /// </summary>
    public static bool IsKnown(string code) => Codes.Contains((code ?? "").Trim());

    /// <summary>The stored spelling for a code that arrived in any casing.</summary>
    public static string Canonical(string code)
    {
        var text = (code ?? "").Trim();
        foreach (var vehicle in All)
        {
            if (string.Equals(vehicle.Code, text, StringComparison.OrdinalIgnoreCase)) return vehicle.Code;
        }
        return text;
    }
}
