using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Scmos.Api.Rules;

namespace Scmos.Api.Ai.Booking;

/// <summary>What the model proposed for one field: the value, and the words of the text it came from.</summary>
public sealed record BookingProposal(string Value, string Quote);

/// <param name="Proposed">What the model said, kept so a person sees what was not accepted.</param>
/// <param name="Value">What goes into the form: the proposal once checked (a customer in the register's spelling), or "" when it was not.</param>
/// <param name="Reason">Why it was not accepted, in Thai; "" when it was.</param>
public sealed record BookingFieldReading(string Field, string Proposed, string Quote, bool Verified, string Value, string Reason);

/// <param name="Missing">The essential fields with no verified value — the draft needs a person for these.</param>
public sealed record BookingDraft(string Category, IReadOnlyList<BookingFieldReading> Fields, IReadOnlyList<string> Missing)
{
    public const string Complete = "COMPLETE";
    public const string NeedsInformation = "NEEDS_INFORMATION";
    public string Status => Missing.Count == 0 ? Complete : NeedsInformation;
    public IReadOnlyDictionary<string, string> Verified =>
        Fields.Where(one => one.Verified).ToDictionary(one => one.Field, one => one.Value, StringComparer.Ordinal);
}

/// <summary>
/// The Booking Agent's check on what the model read (AI Agent Platform
/// specification §27): a proposed field is kept only when the words it cites
/// are really in the text and really say that value — so nothing the text
/// does not say reaches the add-job form. A customer must also be one the
/// register already knows. Everything else is shown to the person as
/// "not accepted", with the words and the reason, and left for them to key.
///
/// <para>
/// The fields are the add-job form's own (<see cref="Services.DocumentExtractor"/>'s
/// lists) less the carrier's side — who hauls it, the truck, the driver — which
/// a booking request does not carry and the Carrier Agent helps choose.
/// </para>
///
/// <para>
/// Dates are read the ways the department's mail writes them — 29/09/2026,
/// 29-9-69 (Buddhist year), 2026-09-29, 29 Sep 2026, 29 ก.ย. 69, and
/// "today / tomorrow / พรุ่งนี้ / มะรืน" against the day the text was received.
/// A date with no year takes the received year only when that lands within a
/// week before and four months after it. Times: 08:30, 8.30, 8am, 0800 น.,
/// 8 โมงเช้า, บ่าย 2, 2 ทุ่ม, เที่ยง; a bare "5 โมง" is ambiguous and is not
/// read. Anything the reader cannot confirm from the words is not accepted.
/// </para>
/// </summary>
public static partial class BookingVerification
{
    public static readonly IReadOnlyDictionary<string, string[]> Fields = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["IMPORT"] = ["customer", "jobCode", "product", "destination", "date", "planTime", "type", "cyYard", "weight", "container", "emptyReturn"],
        ["EXPORT"] = ["customer", "booking", "abs", "fclLcl", "plant", "date", "planTime", "type", "cyYard", "returnLoc", "closingDate", "closingTime", "container", "seal"],
        ["DELIVERY"] = ["customer", "wh", "jobNo", "sid", "date", "province", "zip", "pallet", "kgs", "remark"],
    };

    /// <summary>What a draft cannot be a booking without — the spec's "must not guess" list as the form holds it.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Essential = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["IMPORT"] = ["customer", "date", "type", "destination"],
        ["EXPORT"] = ["customer", "date", "type", "plant"],
        ["DELIVERY"] = ["customer", "date", "wh"],
    };

    private static readonly HashSet<string> DateFields = ["date", "closingDate"];
    private static readonly HashSet<string> TimeFields = ["planTime", "closingTime"];
    private static readonly HashSet<string> NumberFields = ["weight", "kgs", "pallet", "zip"];

    public const int MaxQuote = 300;
    public const int MaxValue = 120;

    public static string CategoryOf(string? category) =>
        (category ?? "").Trim().ToUpperInvariant() is var wanted && Fields.ContainsKey(wanted) ? wanted : "IMPORT";

    public static BookingDraft Check(string category, string text, IReadOnlyDictionary<string, BookingProposal> proposals,
        IReadOnlyCollection<string> knownCustomers, DateOnly received)
    {
        category = CategoryOf(category);
        var source = Flat(text);
        var readings = new List<BookingFieldReading>();
        foreach (var field in Fields[category])
        {
            var proposal = proposals.TryGetValue(field, out var found) ? found : new BookingProposal("", "");
            var value = OneLine(proposal.Value);
            var quote = OneLine(proposal.Quote);
            if (value.Length == 0 || value is "-" || value.Equals("n/a", StringComparison.OrdinalIgnoreCase))
            {
                readings.Add(new(field, "", quote, false, "", "ไม่พบในข้อความ"));
                continue;
            }
            string? problem =
                value.Length > MaxValue || quote.Length > MaxQuote ? "ยาวเกินกว่าจะเป็นค่าเดียว"
                : quote.Length == 0 || !source.Contains(Flat(quote), StringComparison.OrdinalIgnoreCase) ? "ข้อความที่อ้างไม่อยู่ในต้นฉบับ"
                : !Supports(field, value, quote, received) ? "ค่าที่อ่านได้ไม่ตรงกับข้อความที่อ้าง"
                : null;
            var accepted = value;
            if (problem is null && field == "customer")
            {
                var known = knownCustomers.FirstOrDefault(one => Key(one) == Key(value));
                if (known is null) problem = "ไม่พบชื่อลูกค้านี้ในทะเบียน";
                else accepted = known.Trim();
            }
            // The form's type is the department's list; a spelling that does not land on it is the person's to choose.
            if (problem is null && field == "type")
            {
                var code = JobVehicleType.Canonical(value);
                if (JobVehicleType.IsKnown(code)) accepted = code;
                else problem = "ประเภทรถไม่อยู่ในรายการ";
            }
            readings.Add(problem is null
                ? new(field, value, quote, true, accepted, "")
                : new(field, value, quote, false, "", problem));
        }
        var missing = Essential[category].Where(field => !readings.Any(one => one.Field == field && one.Verified)).ToList();
        return new BookingDraft(category, readings, missing);
    }

    /// <summary>Whether the cited words say this value, by the field's kind.</summary>
    public static bool Supports(string field, string value, string quote, DateOnly received)
    {
        if (DateFields.Contains(field))
            return Formats.ParseDay(value) is { } day && DatesIn(quote, received).Contains(day);
        if (TimeFields.Contains(field))
            return TimeOf(value) is { } time && TimesIn(quote).Contains(time);
        if (NumberFields.Contains(field))
        {
            var wanted = Numbers(value);
            return wanted.Count > 0 && wanted.All(Numbers(quote).Contains);
        }
        var key = Key(value);
        return key.Length > 0 && Key(quote).Contains(key, StringComparison.Ordinal);
    }

    /* ------------------------------------------------------------ dates */

    private static readonly string[] EnglishMonths = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];
    private static readonly string[][] ThaiMonths =
    [
        ["มกราคม", "ม.ค."], ["กุมภาพันธ์", "ก.พ."], ["มีนาคม", "มี.ค."], ["เมษายน", "เม.ย."], ["พฤษภาคม", "พ.ค."], ["มิถุนายน", "มิ.ย."],
        ["กรกฎาคม", "ก.ค."], ["สิงหาคม", "ส.ค."], ["กันยายน", "ก.ย."], ["ตุลาคม", "ต.ค."], ["พฤศจิกายน", "พ.ย."], ["ธันวาคม", "ธ.ค."],
    ];

    [GeneratedRegex(@"(?<![\d])(\d{1,2})[/\-.](\d{1,2})[/\-.](\d{2}|\d{4})(?![\d])")]
    private static partial Regex DayMonthYear();
    [GeneratedRegex(@"(?<![\d])(\d{4})-(\d{1,2})-(\d{1,2})(?![\d])")]
    private static partial Regex IsoDate();
    [GeneratedRegex(@"(?<![\d/\-.])(\d{1,2})[/\-](\d{1,2})(?![\d/\-.])")]
    private static partial Regex DayMonth();
    [GeneratedRegex(@"(?<![\d])(\d{1,2})\s*([A-Za-z]{3,9})\.?,?\s*(\d{4}|\d{2})?(?![\d])")]
    private static partial Regex DayEnglishMonth();

    /// <summary>Every day the words name, read against the day the text was received.</summary>
    public static HashSet<DateOnly> DatesIn(string text, DateOnly received)
    {
        var days = new HashSet<DateOnly>();
        var flat = Flat(text);
        void Add(int year, int month, int day)
        {
            if (month is >= 1 and <= 12 && day >= 1 && year is >= 2000 and <= 2100 && day <= DateTime.DaysInMonth(year, month))
                days.Add(new DateOnly(year, month, day));
        }
        void AddNoYear(int month, int day)
        {
            if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(received.Year, month)) return;
            var candidate = new DateOnly(received.Year, month, day);
            if (candidate >= received.AddDays(-7) && candidate <= received.AddMonths(4)) days.Add(candidate);
        }
        foreach (Match m in DayMonthYear().Matches(flat))
            Add(Year(m.Groups[3].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value));
        foreach (Match m in IsoDate().Matches(flat))
            Add(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
        foreach (Match m in DayMonth().Matches(flat))
            AddNoYear(int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value));
        foreach (Match m in DayEnglishMonth().Matches(flat))
        {
            if (MonthOf(m.Groups[2].Value) is not (> 0 and var month)) continue;
            if (m.Groups[3].Success) Add(Year(m.Groups[3].Value), month, int.Parse(m.Groups[1].Value));
            else AddNoYear(month, int.Parse(m.Groups[1].Value));
        }
        for (var i = 0; i < ThaiMonths.Length; i++)
            foreach (var name in ThaiMonths[i])
                foreach (Match m in Regex.Matches(flat, @"(?<![\d])(\d{1,2})\s*" + Regex.Escape(name) + @"\s*(\d{4}|\d{2})?(?![\d])"))
                {
                    if (m.Groups[2].Success) Add(Year(m.Groups[2].Value), i + 1, int.Parse(m.Groups[1].Value));
                    else AddNoYear(i + 1, int.Parse(m.Groups[1].Value));
                }
        var lower = flat.ToLowerInvariant();
        if (lower.Contains("today") || flat.Contains("วันนี้")) days.Add(received);
        if (lower.Contains("tomorrow") || flat.Contains("พรุ่งนี้")) days.Add(received.AddDays(1));
        if (flat.Contains("มะรืน")) days.Add(received.AddDays(2));
        return days;
    }

    /// <summary>An English month name or its three-letter form (and "Sept"), 1–12, or 0 for any other word.</summary>
    private static int MonthOf(string name)
    {
        name = name.ToLowerInvariant();
        if (name == "sept") return 9;
        for (var i = 0; i < 12; i++)
            if (name == EnglishMonths[i] || name == CultureInfo.InvariantCulture.DateTimeFormat.MonthNames[i].ToLowerInvariant()) return i + 1;
        return 0;
    }

    /// <summary>A written year as the Gregorian one: 2569 and 69 are Buddhist (−543), 2026 and 26 are not.</summary>
    private static int Year(string written)
    {
        var year = int.Parse(written);
        return written.Length == 2 ? (year >= 60 ? 2500 + year - 543 : 2000 + year) : year >= 2400 ? year - 543 : year;
    }

    /* ------------------------------------------------------------ times */

    // Not the day and month of a date (12.09.2026), and not the hour of an am/pm time, which AmPm reads.
    [GeneratedRegex(@"(?<![\d.:/\-])(\d{1,2})[:.](\d{2})(?![\d]|[.:/\-]\d|\s*[AaPp]\.?[Mm])")]
    private static partial Regex ClockTime();
    [GeneratedRegex(@"(?<![\d])(\d{1,2})(?:[:.](\d{2}))?\s*(am|pm|a\.m\.|p\.m\.)", RegexOptions.IgnoreCase)]
    private static partial Regex AmPm();
    [GeneratedRegex(@"(?<![\d])(\d{2})(\d{2})\s*(?:น\.|นาฬิกา|hrs|hr|h)(?![A-Za-z])")]
    private static partial Regex MilitaryTime();
    // An hour alone before น. — not the minutes of 08.00 น., which ClockTime reads.
    [GeneratedRegex(@"(?<![\d.:])(\d{1,2})\s*(?:น\.|นาฬิกา)")]
    private static partial Regex ThaiHour();

    /// <summary>The time a value names, as minutes after midnight, or null.</summary>
    public static int? TimeOf(string value) =>
        ClockTime().Match(Flat(value)) is { Success: true } m && int.Parse(m.Groups[1].Value) is var h and < 24 && int.Parse(m.Groups[2].Value) is var min and < 60
            ? h * 60 + min : null;

    /// <summary>Every time of day the words name, as minutes after midnight.</summary>
    public static HashSet<int> TimesIn(string text)
    {
        var times = new HashSet<int>();
        var flat = Flat(text);
        void Add(int hour, int minute) { if (hour is >= 0 and < 24 && minute is >= 0 and < 60) times.Add(hour * 60 + minute); }
        foreach (Match m in ClockTime().Matches(flat)) Add(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
        foreach (Match m in AmPm().Matches(flat))
        {
            var hour = int.Parse(m.Groups[1].Value) % 12;
            if (m.Groups[3].Value.StartsWith('p') || m.Groups[3].Value.StartsWith('P')) hour += 12;
            Add(hour, m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0);
        }
        foreach (Match m in MilitaryTime().Matches(flat)) Add(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
        foreach (Match m in ThaiHour().Matches(flat)) Add(int.Parse(m.Groups[1].Value), 0);
        foreach (Match m in Regex.Matches(flat, @"(?<![\d])(\d{1,2})\s*โมงเช้า")) if (int.Parse(m.Groups[1].Value) is >= 6 and <= 11 and var h) Add(h, 0);
        foreach (Match m in Regex.Matches(flat, @"(?<![\d])(\d{1,2})\s*โมงเย็น")) if (int.Parse(m.Groups[1].Value) is >= 1 and <= 6 and var h) Add(h + 12, 0);
        foreach (Match m in Regex.Matches(flat, @"บ่าย\s*(\d{1,2})")) if (int.Parse(m.Groups[1].Value) is >= 1 and <= 5 and var h) Add(h + 12, 0);
        foreach (Match m in Regex.Matches(flat, @"(?<![\d])(\d{1,2})\s*ทุ่ม")) if (int.Parse(m.Groups[1].Value) is >= 1 and <= 5 and var h) Add(h + 18, 0);
        // A bare "N โมง" is read only where it cannot be the afternoon: seven to eleven in the morning.
        foreach (Match m in Regex.Matches(flat, @"(?<![\d])(\d{1,2})\s*โมง(?!เช้า|เย็น)")) if (int.Parse(m.Groups[1].Value) is >= 7 and <= 11 and var h) Add(h, 0);
        if (flat.Contains("เที่ยง") && !flat.Contains("เที่ยงคืน")) Add(12, 0);
        return times;
    }

    /* ------------------------------------------------------------ text */

    /// <summary>The numbers the words hold, thousands separators removed: "20,000 kg" → 20000.</summary>
    public static HashSet<string> Numbers(string text) =>
        Regex.Matches(Flat(text), @"\d[\d,]*(?:\.\d+)?").Select(m => m.Value.Replace(",", "").TrimEnd('.'))
            .Select(one => one.Contains('.') ? one.TrimEnd('0').TrimEnd('.') : one).ToHashSet(StringComparer.Ordinal);

    /// <summary>Letters, digits and their marks only, case folded: how a value is found inside the words it cites.</summary>
    public static string Key(string text)
    {
        var builder = new StringBuilder();
        foreach (var ch in Flat(text).ToLowerInvariant())
            if (char.IsLetterOrDigit(ch) || CharUnicodeInfo.GetUnicodeCategory(ch) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
                builder.Append(ch);
        return builder.ToString();
    }

    /// <summary>Normalized, one line, single spaces.</summary>
    private static string Flat(string text) =>
        Regex.Replace((text ?? "").Normalize(NormalizationForm.FormKC), @"\s+", " ").Trim();

    private static string OneLine(string text) =>
        new string(Flat(text).Where(ch => !char.IsControl(ch)).ToArray());
}
