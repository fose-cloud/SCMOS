using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Data;

/// <summary>Phase 1 authorization, calendar, configuration, schema and audit checks.</summary>
public static class CarrierBillingFoundationCheck
{
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-carrier-billing-foundation")) return null;

        var failed = 0;
        var overrides = new Dictionary<DateOnly, string>
        {
            [new(2026, 12, 5)] = BusinessCalendarKind.SpecialWorkingDay, // Saturday made working
            [new(2026, 12, 7)] = BusinessCalendarKind.PublicHoliday,    // Monday made holiday
        };

        failed += Check("ordinary Monday is working",
            BusinessCalendar.IsWorkingDay(new(2026, 12, 14), overrides), true);
        failed += Check("ordinary Saturday is not working",
            BusinessCalendar.IsWorkingDay(new(2026, 12, 12), overrides), false);
        failed += Check("special Saturday is working",
            BusinessCalendar.IsWorkingDay(new(2026, 12, 5), overrides), true);
        failed += Check("public-holiday Monday is not working",
            BusinessCalendar.IsWorkingDay(new(2026, 12, 7), overrides), false);

        var day0 = BusinessCalendar.Calculate(new(2026, 12, 4), 4, BillingSlaStartDay.Day0, overrides);
        var day1 = BusinessCalendar.Calculate(new(2026, 12, 4), 4, BillingSlaStartDay.Day1, overrides);
        failed += Check("Day 0 counts the delivery working day", day0.StartDate, new DateOnly(2026, 12, 4));
        failed += Check("Day 0 respects special working day and holiday", day0.DueDate, new DateOnly(2026, 12, 9));
        failed += Check("Day 1 begins at the next working day", day1.StartDate, new DateOnly(2026, 12, 5));
        failed += Check("Day 1 produces a different due date", day1.DueDate, new DateOnly(2026, 12, 10));

        failed += Check("missing SLA start mode is refused",
            BillingSlaRules.Problem("STANDARD", "", 4, new(2026, 1, 1), null) is not null, true);
        failed += Check("only the confirmed 3-4 working-day target is accepted",
            BillingSlaRules.Problem("STANDARD", BillingSlaStartDay.Day0, 5, new(2026, 1, 1), null) is not null, true);
        failed += Check("an explicit valid rule is accepted",
            BillingSlaRules.Problem("STANDARD", BillingSlaStartDay.Day1, 4, new(2026, 1, 1), null), null);

        var carrier = new CarrierTenant(10, "Carrier A",
            new HashSet<string>(["Carrier A", "A LOGISTICS"], StringComparer.OrdinalIgnoreCase));
        failed += Check("carrier owns its supplier", CarrierTenantPolicy.OwnsSupplier(carrier, 10), true);
        failed += Check("carrier cannot select another supplier", CarrierTenantPolicy.OwnsSupplier(carrier, 11), false);
        failed += Check("carrier owns jobs under a registered alias", CarrierTenantPolicy.OwnsJob(carrier, "a logistics"), true);
        failed += Check("carrier cannot read a competitor job", CarrierTenantPolicy.OwnsJob(carrier, "Carrier B"), false);
        failed += Check("missing tenant fails closed", CarrierTenantPolicy.OwnsJob(null, "Carrier A"), false);

        var carrierUser = new AppUser("carrier-check", "carrier@example.test", "Carrier Check",
            Roles.Subcontractor, "SUB-CHECK", "test", true);
        var adminUser = new AppUser("admin-check", "admin@example.test", "Admin Check",
            Roles.Admin, "AD-CHECK", "test", true);
        failed += Check("only the subcontractor role enters the carrier tenant boundary",
            CarrierTenantContext.IsCarrier(carrierUser), true);
        failed += Check("an internal administrator is not accidentally tenant-filtered",
            CarrierTenantContext.IsCarrier(adminUser), false);
        failed += Check("a carrier cannot administer billing configuration",
            carrierUser.Can(Capability.AdministerData), false);
        failed += Check("an administrator can maintain billing configuration",
            adminUser.Can(Capability.AdministerData), true);

        var options = new DbContextOptionsBuilder<ScmosDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=scmos-foundation-model;Trusted_Connection=True")
            .Options;
        using var db = new ScmosDbContext(options);
        var calendar = db.Model.FindEntityType(typeof(BusinessCalendarDay));
        var sla = db.Model.FindEntityType(typeof(BillingSlaRule));
        failed += Check("calendar maps to its additive table", calendar?.GetTableName(), "business_calendar_days");
        failed += Check("SLA maps to its additive table", sla?.GetTableName(), "billing_sla_rules");
        failed += Check("SLA rule has unique code/effective-date identity",
            sla?.GetIndexes().Any(index => index.IsUnique
                && index.Properties.Select(property => property.Name).SequenceEqual([nameof(BillingSlaRule.Code), nameof(BillingSlaRule.EffectiveFrom)])) == true, true);
        failed += Check("configuration has a dedicated audit action", AuditActions.Configure, "configure");

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.42, 10.0.0.8";
        var audit = new AuditService(db, new HttpContextAccessor { HttpContext = context },
            NullLogger<AuditService>.Instance);
        var administrator = new AppUser("phase1-check", "admin@example.test", "Phase 1 Check",
            Roles.Admin, "OP-CHECK", "test", true);
        audit.Stage(administrator, AuditActions.Configure, "billing-sla-rule", "STANDARD:2026-01-01",
            "STANDARD", "rule", "", "day1|4||True", "phase 1 check");
        var auditRow = db.ChangeTracker.Entries<AuditEvent>()
            .Single(entry => entry.State == EntityState.Added).Entity;
        failed += Check("configuration audit is staged in the caller transaction", auditRow.Action,
            AuditActions.Configure);
        failed += Check("configuration audit keeps the authenticated actor", auditRow.Who,
            administrator.Signature);
        failed += Check("configuration audit records the forwarded client address", auditRow.IpAddress,
            "203.0.113.42");

        Console.WriteLine(failed == 0
            ? "Carrier Billing Phase 1 foundation checks passed."
            : $"{failed} Carrier Billing Phase 1 foundation check(s) failed.");
        return failed == 0 ? 0 : 1;
    }

    private static int Check<T>(string why, T got, T want)
    {
        var ok = EqualityComparer<T>.Default.Equals(got, want);
        Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {why}");
        if (!ok) Console.WriteLine($"       got {got}; want {want}");
        return ok ? 0 : 1;
    }
}
