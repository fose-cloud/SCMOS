namespace Scmos.Api.Rules;

/// <summary>
/// When a driver's training stops counting.
///
/// The whole module turns on one calculation — days between today and the
/// expiry date — so it lives here and nowhere else. The dashboard's tiles, the
/// matrix's colours, the alert that fires at sixty days and the refusal at
/// assignment all read these same four bands. Written twice they would drift,
/// and the drift would show up as a driver the dashboard calls valid and the
/// assignment screen refuses.
///
/// The bands are the customer's, not ours:
///
///   more than 60 days   Valid
///   31 to 60 days       Attention
///   1 to 30 days        Expiring Soon
///   today or past       Expired
///
/// Nothing recalculates on a timer. The status is derived from the date every
/// time it is asked for, so it changes at midnight without anything running —
/// a scheduled job that failed quietly would otherwise leave a expired
/// certificate reading as valid for as long as nobody noticed.
/// </summary>
public static class TrainingRules
{
    public const string Valid = "VALID";
    public const string Attention = "ATTENTION";
    public const string ExpiringSoon = "EXPIRING_SOON";
    public const string Expired = "EXPIRED";

    /// <summary>Training a customer requires that the driver has never taken.</summary>
    public const string Missing = "MISSING";

    /// <summary>In the order they appear on the dashboard, worst last.</summary>
    public static readonly string[] All = [Valid, Attention, ExpiringSoon, Expired, Missing];

    public static readonly IReadOnlyDictionary<string, string> Thai = new Dictionary<string, string>
    {
        [Valid] = "ยังใช้ได้",
        [Attention] = "ใกล้ครบกำหนด",
        [ExpiringSoon] = "ใกล้หมดอายุ",
        [Expired] = "หมดอายุแล้ว",
        [Missing] = "ยังไม่เคยอบรม",
    };

    /// <summary>
    /// Days from <paramref name="today"/> until the certificate lapses.
    /// Negative once it has. Null when the date cannot be read, which is not
    /// the same as expired and must not be shown as either.
    /// </summary>
    public static int? DaysLeft(string expiryDate, DateOnly today)
    {
        var expiry = ParseDate(expiryDate);
        return expiry is null ? null : expiry.Value.DayNumber - today.DayNumber;
    }

    /// <summary>
    /// The band a certificate falls in, or null when its date is unreadable.
    ///
    /// An unreadable date returns null rather than Expired on purpose. Calling
    /// it expired would put a driver in the refusal list over a typo, and the
    /// person fixing it would have no way to tell that case apart from a
    /// genuinely lapsed certificate.
    /// </summary>
    public static string? Status(string expiryDate, DateOnly today)
    {
        var days = DaysLeft(expiryDate, today);
        return days switch
        {
            null => null,
            <= 0 => Expired,
            <= 30 => ExpiringSoon,
            <= 60 => Attention,
            _ => Valid,
        };
    }

    /// <summary>Whether a driver may run this work today.</summary>
    public static bool IsEligible(string? status) => status is Valid or Attention or ExpiringSoon;

    /// <summary>
    /// The days before expiry that somebody should be told.
    ///
    /// Four warnings, widening as it gets closer. Sixty days is enough to book
    /// a course; seven is enough to stop planning that driver onto the work.
    /// </summary>
    public static readonly int[] AlertDays = [60, 30, 14, 7];

    /// <summary>
    /// Whether today is one of the days this certificate should raise an alert.
    ///
    /// Exact matches only, so a certificate does not alert every day for two
    /// months — four notices that get read beat sixty that get filtered.
    /// </summary>
    public static bool AlertsToday(string expiryDate, DateOnly today)
    {
        var days = DaysLeft(expiryDate, today);
        return days is not null && (AlertDays.Contains(days.Value) || days.Value == 0);
    }

    /// <summary>
    /// Training compliance, as the proportion of required training that is
    /// currently valid.
    ///
    /// Missing training counts against it. That is the point: a driver who has
    /// never taken a course the customer demands is exactly as unable to run
    /// the work as one whose certificate lapsed yesterday, and a percentage
    /// that ignored the first would read best for the carriers who record
    /// least.
    ///
    /// Returns null when nothing is required at all, rather than 100 — no
    /// requirement is not perfect compliance, it is nothing to measure.
    /// </summary>
    public static double? Compliance(int validRequired, int totalRequired) =>
        totalRequired <= 0 ? null : Math.Round(validRequired * 100.0 / totalRequired, 1);

    /// <summary>
    /// Reads the dates this system stores: <c>dd/MM/yyyy</c> as the plan
    /// workbooks write them, or ISO as a form posts them.
    /// </summary>
    public static DateOnly? ParseDate(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) return null;

        foreach (var format in new[] { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd" })
        {
            if (DateOnly.TryParseExact(text, format,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    /// <summary>Written back the way the rest of the register writes dates.</summary>
    public static string Write(DateOnly day) => Formats.PlanDate(day);
}
