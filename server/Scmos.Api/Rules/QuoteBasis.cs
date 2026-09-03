namespace Scmos.Api.Rules;

/// <summary>
/// How an extra charge on a quotation applies.
///
/// A controlled list, and the reason for it is sitting in the older
/// <c>rate_surcharges</c> table: its unit is a typed string, so one idea is
/// free to arrive as "ต่อชั่วโมง", "per hr" and "/hour" and nothing can add the
/// three of them together. A quotation has to reconcile — every line has to be
/// arrived at the same way twice — so the four ways a charge can apply are named
/// here and anything else is refused on the way in.
/// </summary>
public static class QuoteBasis
{
    public const string Flat = "flat";
    public const string PerKm = "perKm";
    public const string PerHour = "perHour";
    public const string Percent = "percent";

    public static readonly string[] All = [Flat, PerKm, PerHour, Percent];

    /// <summary>The basis as written, or empty when it is not one of the four.</summary>
    public static string Read(string? value)
    {
        var wanted = (value ?? "").Trim();
        return All.FirstOrDefault(one =>
            string.Equals(one, wanted, StringComparison.OrdinalIgnoreCase)) ?? "";
    }

    public static readonly Dictionary<string, string> Thai = new()
    {
        [Flat] = "เหมาต่อเที่ยว",
        [PerKm] = "ต่อกิโลเมตร",
        [PerHour] = "ต่อชั่วโมง",
        [Percent] = "เปอร์เซ็นต์ของต้นทุน",
    };
}
