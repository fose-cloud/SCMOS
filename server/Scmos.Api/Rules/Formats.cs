using System.Text.RegularExpressions;

namespace Scmos.Api.Rules;

/// <summary>
/// The data standard, in the language that now owns it.
///
/// These are the formats the register is judged against: a date that will not
/// parse drops its job out of every KPI, so the rule that decides "will not
/// parse" has to be one rule, in one place. This is the C# side of what
/// app/scmos/standard.ts has done in the browser — the first piece moved under
/// the architecture's decision that business rules live in .NET.
///
/// The regexes are the same expressions, character for character, so a value the
/// browser accepts is a value this accepts. Where they must both exist during
/// the port, that identity is the contract between them.
/// </summary>
public static partial class Formats
{
    [GeneratedRegex(@"^\d{2}/\d{2}/\d{4}$")]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"^([01]\d|2[0-3]):[0-5]\d$")]
    private static partial Regex TimePattern();

    [GeneratedRegex(@"^[A-Z]{4}\d{7}$")]
    private static partial Regex ContainerPattern();

    /// <summary>
    /// The operators' several ways of writing "there isn't one". Kept identical
    /// to BLANK_VALUE in app/scmos/standard.ts: this list had drifted from it —
    /// the browser blanked "--" and "na" and this did not, this blanked an en
    /// dash and "null" and the browser did not — so the same cell could be empty
    /// on screen and malformed in the register.
    /// </summary>
    [GeneratedRegex(@"^(-+|—|–|n/?a|none|null|ไม่มี)$", RegexOptions.IgnoreCase)]
    private static partial Regex BlankPattern();

    /// <summary>
    /// Zero-width and non-breaking characters pasted in from Excel and LINE.
    /// One real driver number ends in U+200B, which silently fails every match.
    /// The browser has stripped these since the standard was written; this did
    /// not, so that number was accepted there and flagged here.
    /// </summary>
    [GeneratedRegex(@"[\u200B-\u200D\uFEFF\u00A0]")]
    private static partial Regex InvisiblePattern();

    [GeneratedRegex(@"^\d+(\.\d+)?$")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"^0\d{1,2}-\d{7,8}$")]
    private static partial Regex PhonePattern();

    /// <summary>
    /// A Thai plate without its province: digits, Thai letters, or the modern
    /// digit-plus-two-letters form, then three or four digits.
    /// </summary>
    [GeneratedRegex(@"^([0-9]{1,3}|[ก-ฮ]{1,3}|[0-9][ก-ฮ]{2})[-\s]?\d{3,4}$")]
    private static partial Regex PlatePattern();

    /// <summary>The province a plate is registered in, written after the number.</summary>
    [GeneratedRegex(@"[\s.]*[฀-๿][฀-๿\s.]*$")]
    private static partial Regex ProvincePattern();

    public static bool IsDate(string value) => DatePattern().IsMatch(value);
    public static bool IsTime(string value) => TimePattern().IsMatch(value);
    public static bool IsContainer(string value) => ContainerPattern().IsMatch(value);
    public static bool IsNumber(string value) => NumberPattern().IsMatch(value);
    public static bool IsPhone(string value) => PhonePattern().IsMatch(value);
    public static bool IsPlate(string value) => PlatePattern().IsMatch(StripProvince(value));

    public static string StripProvince(string plate) => ProvincePattern().Replace(plate, "").Trim();

    /// <summary>
    /// A value with the placeholders taken out. The plan is full of dashes and
    /// N/A standing in for "nothing", and treating those as data is how a blank
    /// column becomes a false KPI.
    /// </summary>
    public static string Clean(string? value)
    {
        var text = InvisiblePattern().Replace(value ?? "", "").Trim();
        return BlankPattern().IsMatch(text) ? "" : text;
    }

    /// <summary>DD/MM/YYYY to a sortable integer, or 0 when it will not parse.</summary>
    /// <summary>
    /// A date the way the plan writes it: <c>dd/MM/yyyy</c>, Gregorian year.
    ///
    /// The workspace compares a job's date to "today" as text, because that is
    /// what the register stores. Formatting it here rather than at each call
    /// site keeps the comparison honest — one leading zero in the wrong place
    /// and the TODAY tab is empty every day of the month before the tenth.
    /// </summary>
    public static string PlanDate(DateOnly day) => day.ToString("dd/MM/yyyy",
        System.Globalization.CultureInfo.InvariantCulture);

    public static int DateNumber(string? value)
    {
        var text = Clean(value);
        if (text.Length < 10) return 0;
        if (!int.TryParse(text.AsSpan(0, 2), out var day)) return 0;
        if (text[2] != '/' || text[5] != '/') return 0;
        if (!int.TryParse(text.AsSpan(3, 2), out var month)) return 0;
        if (!int.TryParse(text.AsSpan(6, 4), out var year)) return 0;
        return year * 10000 + month * 100 + day;
    }

    /// <summary>HH:MM to minutes since midnight, or null when it will not parse.</summary>
    public static int? TimeMinutes(string? value)
    {
        var text = Clean(value);
        var colon = text.IndexOf(':');
        if (colon < 1 || colon + 3 > text.Length) return null;
        if (!int.TryParse(text.AsSpan(0, colon), out var hours)) return null;
        if (!int.TryParse(text.AsSpan(colon + 1, 2), out var minutes)) return null;
        if (hours is < 0 or > 23 || minutes is < 0 or > 59) return null;
        return hours * 60 + minutes;
    }

    /// <summary>The year and month of a DD/MM/YYYY date, for period filtering.</summary>
    public static (string Year, string Month, string Day) PartsOf(string? value)
    {
        var text = Clean(value);
        if (text.Length < 10 || text[2] != '/' || text[5] != '/') return ("", "", "");
        return (text[6..10], text[3..5], text[0..2]);
    }
}
