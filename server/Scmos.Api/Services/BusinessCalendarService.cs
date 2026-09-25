using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record BillingSlaResolution(bool Ok, string Code, string Message,
    BillingSlaRule? Rule = null, BillingSlaDates? Dates = null);

/// <summary>
/// Reads the reviewed calendar and effective SLA configuration. It refuses a
/// missing or ambiguous rule; no appsetting or code default supplies the TBD
/// Day 0/Day 1 answer.
/// </summary>
public class BusinessCalendarService(ScmosDbContext db)
{
    public async Task<IReadOnlyDictionary<DateOnly, string>> OverridesAsync(
        DateOnly from, DateOnly to, CancellationToken token) =>
        await db.BusinessCalendarDays.AsNoTracking()
            .Where(row => row.Date >= from && row.Date <= to)
            .ToDictionaryAsync(row => row.Date, row => row.Kind, token);

    public async Task<BillingSlaResolution> CalculateAsync(string ruleCode,
        DateOnly deliveredOn, CancellationToken token)
    {
        var code = (ruleCode ?? "").Trim().ToUpperInvariant();
        var rules = await db.BillingSlaRules.AsNoTracking()
            .Where(row => row.Active && row.Code == code
                && row.EffectiveFrom <= deliveredOn
                && (row.EffectiveTo == null || row.EffectiveTo >= deliveredOn))
            .OrderByDescending(row => row.EffectiveFrom)
            .ToListAsync(token);

        if (rules.Count == 0)
            return new(false, "BILLING_SLA_RULE_NOT_FOUND",
                $"ไม่พบกฎ SLA {code} ที่ใช้ได้ในวันที่ {deliveredOn:yyyy-MM-dd}");
        if (rules.Count > 1)
            return new(false, "MULTIPLE_BILLING_SLA_RULES",
                $"พบกฎ SLA {code} ซ้อนกัน {rules.Count} รายการ");

        var rule = rules[0];
        // Do not use a fixed horizon: a long company shutdown must not silently
        // become ordinary weekdays merely because it extends past an estimate.
        var calendar = await db.BusinessCalendarDays.AsNoTracking()
            .Where(row => row.Date >= deliveredOn)
            .ToDictionaryAsync(row => row.Date, row => row.Kind, token);
        var dates = BusinessCalendar.Calculate(deliveredOn, rule.TargetWorkingDays, rule.StartDay, calendar);
        return new(true, "OK", "คำนวณ SLA แล้ว", rule, dates);
    }
}
