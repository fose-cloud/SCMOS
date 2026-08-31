using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// Runs <see cref="JobVehicleType.Canonical"/> against the spellings the
/// register actually contains, with `--check-types`.
///
/// Every spelling below was counted in the real plan, and the awkward ones are
/// here on purpose: the box that says what it is <em>not</em>, the high cube
/// spelled two ways, the count that is not a one, and the note somebody typed
/// into the type column instead of the remark column. The point of the last
/// group is that they come out <b>unchanged</b> — a rule that files everything
/// somewhere is worse than one that admits when it cannot read a row.
/// </summary>
public static class TypeCheck
{
    private static readonly (string Raw, string Expect, string Why)[] Cases =
    [
        // The bulk of the register: the same box, with and without the foot mark.
        ("1X20'", "1X20'", "the trailing foot mark is not a category"),
        ("1X20", "1X20'", "the same box, typed by somebody else"),
        ("1x20", "1X20'", "and again in lower case"),
        ("1X40'", "1X40'", "forty, same story"),
        ("1X40", "1X40'", "forty, same story"),

        // A tall forty, spelled both ways the team spells it.
        ("1X40 HQ", "1X40' HQ", "high cube"),
        ("1X40'HQ", "1X40' HQ", "high cube with the foot mark in the middle"),
        ("1X40HQ'", "1X40' HQ", "high cube with the foot mark at the end"),
        ("1X40HC", "1X40' HQ", "high container is the same tall forty as high cube"),
        ("1X40'HC", "1X40' HQ", "and with a foot mark"),

        // Lorries, by wheels.
        ("1X6WH'", "1X6WH", "the commonest six-wheel spelling in the register"),
        ("1X6W", "1X6WH", "same lorry"),
        ("6 WHEEL", "1X6WH", "written out"),
        ("1X6 Wheels", "1X6WH", "written out and pluralised"),
        ("1X4WH'", "1X4WH", "four wheels"),
        ("4WHEEL", "1X4WH", "four wheels, written out, no count"),
        ("1X10WH'", "1X10WH", "ten wheels — two digits, not a one and a zero"),

        // What was special about the box.
        ("1X20TK", "1X20' TK", "tank"),
        ("1X20' TK", "1X20' TK", "tank, spaced"),
        ("1 X 20� TANK", "1X20' TK", "tank, spelled out, foot mark mangled by an encoding"),
        ("1 X 20 ISOTANK", "1X20' TK", "iso tank"),
        ("1X40 REEFER", "1X40' RF", "reefer, spelled out"),
        ("1X40RF", "1X40' RF", "reefer, abbreviated"),
        ("1X40�FR", "1X40' RF", "reefer with the letters transposed and the mark mangled"),
        ("1X20 OT'", "1X20' OT", "open top"),
        ("1X20DG'", "1X20' DG", "dangerous goods"),
        ("1X40HC DG", "1X40' DG", "dangerous goods wins over the high cube: what it carries decides"),

        // The trap. NON-DG has to be read before DG, or it becomes its opposite.
        ("1x20 NONDG", "1X20'", "not dangerous goods is the ordinary box"),
        ("1x20 NON-DG", "1X20'", "same, hyphenated"),
        ("1x20 NON DG", "1X20'", "same, spaced"),

        // Already right, and the empty column.
        ("1X20' DG", "1X20' DG", "already canonical"),
        ("COMBINE", "COMBINE", "already canonical"),
        ("", "", "an empty type stays empty — nobody guesses one"),

        // The ones nothing should be done to.
        ("3X6W", "3X6W", "three lorries is a count somebody meant, not one lorry"),
        ("1X20 DG >> 1X40 DG", "1X20 DG >> 1X40 DG", "a note about a change of box, left for a person"),
        ("1X20DC", "1X20DC", "DC is not a suffix this knows — left alone rather than guessed"),
    ];

    /// <summary>
    /// Null when this is not the flag being asked for; otherwise the exit code,
    /// so a failing check can stop a build rather than only saying so.
    /// </summary>
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-types")) return null;

        var failed = 0;
        foreach (var (raw, expect, why) in Cases)
        {
            var got = JobVehicleType.Canonical(raw);
            var ok = got == expect;
            if (!ok) failed++;
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")}  {Quote(raw),-24} -> {Quote(got),-12} {(ok ? "" : "expected " + Quote(expect) + "  ")}({why})");
        }

        // The list the dropdown offers has to be reachable by the rule, or the
        // register would hold a value the form cannot re-select.
        foreach (var vehicle in JobVehicleType.All)
        {
            if (JobVehicleType.Canonical(vehicle.Code) == vehicle.Code) continue;
            failed++;
            Console.WriteLine($"FAIL  the list offers {Quote(vehicle.Code)} but the rule rewrites it");
        }

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? $"{Cases.Length} spellings, {JobVehicleType.All.Length} canonical types, all as expected."
            : $"{failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static string Quote(string value) => "\"" + value + "\"";
}
