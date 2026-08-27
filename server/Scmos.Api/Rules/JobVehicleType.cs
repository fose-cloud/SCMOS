using System.Text.RegularExpressions;

namespace Scmos.Api.Rules;

/// <summary>
/// What was sent to do the job — one reading of it.
///
/// The register held sixty-four spellings of about sixteen things. `1X20'` and
/// `1X20` are the same box typed by two people; so are `1X6WH'`, `1X6W`,
/// `6 WHEEL` and `1X6 Wheels`. Nobody meant to create sixty-four categories,
/// but a filter, a KPI grouping and a rate lookup all read that column, and to
/// all three of them a trailing apostrophe was a different kind of lorry.
///
/// <para>
/// This is deliberately <b>not</b> <see cref="RateVehicles"/>. That list is
/// what a price can be quoted against, and it collapses 40' and 40'HQ into one
/// code because they cost the same. This list is what turned up at the gate,
/// where a high cube is a different piece of equipment from a standard forty
/// and the operator keying the row knows which one came. The two answer
/// different questions, so they are two lists — but only these two, and each
/// is written once and served to the browser rather than repeated there.
/// </para>
///
/// <para>
/// <see cref="Canonical"/> is rules rather than a table of the sixty-four
/// spellings that happen to exist today. A table would be right until the next
/// operator types `1 X 20 FT` and silently becomes a sixty-fifth category; the
/// rules read a shape — a size, then what was special about it — so a spelling
/// nobody has used yet still lands in the right place.
/// </para>
/// </summary>
public static class JobVehicleType
{
    /// <param name="Code">What is stored on the job, and what the dropdown offers.</param>
    /// <param name="Label">How it reads to somebody keying a row.</param>
    public readonly record struct VehicleType(string Code, string Label);

    /// <summary>
    /// The whole list, most-used first, because the dropdown is read top down
    /// and two thirds of the register is a plain twenty or a plain forty.
    /// </summary>
    public static readonly VehicleType[] All =
    [
        // A box keeps the foot mark. A lorry is written the long way. Both are
        // the team's own spellings — this column is read by the people who
        // wrote it, so it is written the way they write it.
        new("1X20'", "1X20' ตู้ 20 ฟุต"),
        new("1X40'", "1X40' ตู้ 40 ฟุต"),
        new("1X40' HQ", "1X40' HQ ตู้สูง"),
        new("1X45'", "1X45' ตู้ 45 ฟุต"),

        new("1X20' DG", "1X20' DG วัตถุอันตราย"),
        new("1X40' DG", "1X40' DG วัตถุอันตราย"),

        new("1X20' RF", "1X20' Reefer ตู้เย็น"),
        new("1X40' RF", "1X40' Reefer ตู้เย็น"),

        new("1X20' TK", "1X20' ISO Tank"),
        new("1X40' TK", "1X40' ISO Tank"),

        new("1X20' OT", "1X20' Open Top"),
        new("1X40' OT", "1X40' Open Top"),

        new("1X4WH", "1X4WH รถ 4 ล้อ"),
        new("1X6WH", "1X6WH รถ 6 ล้อ"),
        new("1X10WH", "1X10WH รถ 10 ล้อ"),

        new("COMBINE", "COMBINE รวมงาน"),
    ];

    private static readonly HashSet<string> Codes =
        new(All.Select(vehicle => vehicle.Code), StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a value is already one of the sixteen.</summary>
    public static bool IsKnown(string? code) => Codes.Contains((code ?? "").Trim());

    // A lorry, by wheels: 6W, 1X6WH', 6 WHEEL, 1X6 Wheels, 1X10WH.
    private static readonly Regex Wheels =
        new(@"^(\d{1,2})\s*W(?:H|HEEL|HEELS|HEELER)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // A box, by feet, then whatever was said about it: 20, 40HQ, 40 REEFER, 20' TK.
    //
    // The optional FT has to end on a word boundary. Without one it ate the F
    // of "40 FR" and left an R nothing could read, so a mangled reefer came
    // back untouched.
    private static readonly Regex Box =
        new(@"^(20|40|45)(?:\s*(?:FT|F)\b)?\s*(.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Every word this knows how to read after the size.
    ///
    /// The gate matters more than the list: a rest containing anything not in
    /// here means the column is carrying something other than a type — a
    /// second box, a note, a change of plan — and the whole value is left
    /// alone. Without it "1X20 DG >> 1X40 DG" quietly became "1x20 DG" and the
    /// half that said the box had changed was gone.
    /// </summary>
    private static readonly HashSet<string> Vocabulary = new(StringComparer.OrdinalIgnoreCase)
    {
        "FT", "F", "HQ", "HC", "DG", "NON", "NONDG",
        "RF", "FR", "REEFER", "REEFFER", "TK", "TANK", "ISO", "ISOTANK", "OT",
    };

    /// <summary>
    /// The one of the sixteen a written value means, or the value cleaned up
    /// but otherwise untouched when it means none of them.
    ///
    /// <para>
    /// Unreadable input is left alone rather than forced into the nearest
    /// category. A row that says something this cannot parse is a row somebody
    /// should look at; quietly filing it under `1x20` would hide it and put a
    /// wrong box on a real shipment.
    /// </para>
    /// </summary>
    public static string Canonical(string? raw)
    {
        var text = (raw ?? "").Trim();
        if (text.Length == 0) return "";
        if (IsKnown(text)) return All.First(v => string.Equals(v.Code, text, StringComparison.OrdinalIgnoreCase)).Code;

        // Strip what carries no meaning: the trailing foot mark that half the
        // team types, punctuation, and the replacement characters left behind
        // where a foot mark went through the wrong encoding on its way in.
        var work = text.ToUpperInvariant();
        work = work.Replace('�', ' ').Replace('’', ' ').Replace('\'', ' ').Replace('"', ' ');
        work = work.Replace('.', ' ').Replace('-', ' ').Replace('_', ' ');
        work = Regex.Replace(work, @"\s+", " ").Trim();

        if (work is "COMBINE" or "COMBINED") return "COMBINE";

        // "1X20" and "1 X 20" say one of something; the one is not information.
        // Anything other than one — "3X6W" — is a count somebody meant, so it
        // is left for a person rather than silently reduced to a single lorry.
        var one = Regex.Match(work, @"^1\s*X\s*(.+)$");
        if (one.Success) work = one.Groups[1].Value.Trim();
        else if (Regex.IsMatch(work, @"^\d+\s*X\s")) return text;

        var wheels = Wheels.Match(work);
        if (wheels.Success)
        {
            var code = "1X" + wheels.Groups[1].Value + "WH";
            return IsKnown(code) ? code : text;
        }

        var box = Box.Match(work);
        if (!box.Success) return text;

        var feet = box.Groups[1].Value;
        var rest = box.Groups[2].Value.Trim();

        // One unreadable word and the whole value stays as it was typed.
        if (rest.Length > 0 && rest.Split(' ').Any(word => !Vocabulary.Contains(word))) return text;

        // "NON DG" says what it is not, which is the ordinary box — and it has
        // to be read before "DG", or it would be filed as its own opposite.
        var isNonDg = Regex.IsMatch(rest, @"\bNON\s?DG\b");
        var suffix =
            Regex.IsMatch(rest, @"\b(RF|REEFER|FR|REEFFER)\b") ? "RF"
            : Regex.IsMatch(rest, @"\b(TK|TANK|ISOTANK|ISO TANK)\b") ? "TK"
            : Regex.IsMatch(rest, @"\bOT\b") ? "OT"
            : !isNonDg && Regex.IsMatch(rest, @"\bDG\b") ? "DG"
            // High cube and high container are the same tall forty, and the
            // register spells it both ways.
            : Regex.IsMatch(rest, @"\b(HQ|HC)\b") ? "HQ"
            : rest.Length == 0 || isNonDg ? ""
            : null;

        // Something was said about the box that this does not recognise —
        // a second container number, a note, a change of plan. Left as it is.
        if (suffix is null) return text;

        var built = ("1X" + feet + "'" + (suffix.Length > 0 ? " " + suffix : "")).Trim();
        return IsKnown(built) ? built : text;
    }
}
