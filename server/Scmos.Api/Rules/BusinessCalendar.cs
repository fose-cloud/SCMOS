namespace Scmos.Api.Rules;

public static class BusinessCalendarKind
{
    public const string WorkingDay = "working-day";
    public const string Weekend = "weekend";
    public const string PublicHoliday = "public-holiday";
    public const string CompanyHoliday = "company-holiday";
    public const string SpecialWorkingDay = "special-working-day";

    public static readonly string[] All =
        [WorkingDay, Weekend, PublicHoliday, CompanyHoliday, SpecialWorkingDay];
}

public static class BillingSlaStartDay
{
    public const string Day0 = "day0";
    public const string Day1 = "day1";
    public static readonly string[] All = [Day0, Day1];
}

public record CalendarOverride(DateOnly Date, string Kind);

public record BillingSlaDates(DateOnly StartDate, DateOnly DueDate);

/// <summary>
/// Pure working-day arithmetic. It knows nothing about customers, carriers or
/// invoice state; callers provide the reviewed calendar and the explicit SLA
/// rule. This keeps a missing configuration from becoming a hidden default.
/// </summary>
public static class BusinessCalendar
{
    public static bool IsWorkingDay(DateOnly date, IReadOnlyDictionary<DateOnly, string> overrides)
    {
        if (overrides.TryGetValue(date, out var kind))
            return kind is BusinessCalendarKind.WorkingDay or BusinessCalendarKind.SpecialWorkingDay;

        return date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
    }

    public static DateOnly NextWorkingDay(DateOnly date,
        IReadOnlyDictionary<DateOnly, string> overrides, bool includeDate)
    {
        var candidate = includeDate ? date : date.AddDays(1);
        while (!IsWorkingDay(candidate, overrides)) candidate = candidate.AddDays(1);
        return candidate;
    }

    public static DateOnly AddWorkingDays(DateOnly date, int days,
        IReadOnlyDictionary<DateOnly, string> overrides)
    {
        if (days < 0) throw new ArgumentOutOfRangeException(nameof(days));
        var current = date;
        for (var count = 0; count < days;)
        {
            current = current.AddDays(1);
            if (IsWorkingDay(current, overrides)) count++;
        }
        return current;
    }

    /// <summary>
    /// Day 0 starts on the delivery working day; Day 1 starts on the next
    /// working day. The target is counted from that start, so a four-day rule
    /// has three further working days after its first counted day.
    /// </summary>
    public static BillingSlaDates Calculate(DateOnly deliveredOn, int targetWorkingDays,
        string startDay, IReadOnlyDictionary<DateOnly, string> overrides)
    {
        if (targetWorkingDays is not (3 or 4))
            throw new ArgumentOutOfRangeException(nameof(targetWorkingDays), "Billing SLA must be 3 or 4 working days.");
        if (!BillingSlaStartDay.All.Contains(startDay, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Billing SLA start day must be day0 or day1.", nameof(startDay));

        var start = NextWorkingDay(deliveredOn, overrides,
            includeDate: string.Equals(startDay, BillingSlaStartDay.Day0, StringComparison.OrdinalIgnoreCase));
        return new BillingSlaDates(start, AddWorkingDays(start, targetWorkingDays - 1, overrides));
    }
}

public static class BillingSlaRules
{
    public static string? Problem(string code, string startDay, int targetWorkingDays,
        DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        if (string.IsNullOrWhiteSpace(code)) return "ต้องระบุรหัสกฎ SLA";
        if (!BillingSlaStartDay.All.Contains(startDay, StringComparer.OrdinalIgnoreCase))
            return "วันเริ่ม SLA ต้องเป็น Day 0 หรือ Day 1";
        if (targetWorkingDays is not (3 or 4)) return "SLA ต้องเป็น 3 หรือ 4 วันทำการ";
        if (effectiveTo is { } until && until < effectiveFrom)
            return "วันสิ้นสุดต้องไม่ก่อนวันเริ่มใช้";
        return null;
    }
}
