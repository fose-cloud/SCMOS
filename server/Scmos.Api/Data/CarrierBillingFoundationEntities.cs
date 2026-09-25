using Microsoft.EntityFrameworkCore;

namespace Scmos.Api.Data;

/// <summary>
/// One explicit exception to the normal Monday-Friday calendar.
///
/// Dates not present in this table follow the normal weekday rule. A row only
/// exists when the business has said that a date differs, or when a named
/// holiday must remain visible for audit and administration.
/// </summary>
public class BusinessCalendarDay
{
    public DateOnly Date { get; set; }
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Effective-dated billing SLA configuration. No default row is seeded: the
/// Day 0/Day 1 decision is explicitly TBD and a missing rule must stay visible
/// rather than silently becoming a guessed business answer.
/// </summary>
public class BillingSlaRule
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string StartDay { get; set; } = "";
    public int TargetWorkingDays { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool Active { get; set; } = true;
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class CarrierBillingFoundationModel
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<BusinessCalendarDay>(entry =>
        {
            entry.ToTable("business_calendar_days", table => table.HasCheckConstraint(
                "business_calendar_days_kind_ck",
                "[kind] IN ('working-day','weekend','public-holiday','company-holiday','special-working-day')"));
            entry.HasKey(row => row.Date);
            entry.Property(row => row.Date).HasColumnName("date").HasColumnType("date");
            entry.Property(row => row.Kind).HasColumnName("kind").HasMaxLength(24);
            entry.Property(row => row.Name).HasColumnName("name").HasMaxLength(160).HasDefaultValue("");
            entry.Property(row => row.UpdatedBy).HasColumnName("updated_by").HasMaxLength(120);
            entry.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entry.HasIndex(row => row.Kind).HasDatabaseName("business_calendar_days_kind_idx");
        });

        model.Entity<BillingSlaRule>(entry =>
        {
            entry.ToTable("billing_sla_rules", table =>
            {
                table.HasCheckConstraint("billing_sla_rules_start_day_ck", "[start_day] IN ('day0','day1')");
                table.HasCheckConstraint("billing_sla_rules_target_ck", "[target_working_days] IN (3,4)");
                table.HasCheckConstraint("billing_sla_rules_dates_ck",
                    "[effective_to] IS NULL OR [effective_to] >= [effective_from]");
            });
            entry.HasKey(row => row.Id);
            entry.Property(row => row.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(row => row.Code).HasColumnName("code").HasMaxLength(40);
            entry.Property(row => row.StartDay).HasColumnName("start_day").HasMaxLength(4);
            entry.Property(row => row.TargetWorkingDays).HasColumnName("target_working_days");
            entry.Property(row => row.EffectiveFrom).HasColumnName("effective_from").HasColumnType("date");
            entry.Property(row => row.EffectiveTo).HasColumnName("effective_to").HasColumnType("date");
            entry.Property(row => row.Active).HasColumnName("active").HasDefaultValue(true);
            entry.Property(row => row.UpdatedBy).HasColumnName("updated_by").HasMaxLength(120);
            entry.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entry.HasIndex(row => new { row.Code, row.EffectiveFrom }).IsUnique()
                .HasDatabaseName("billing_sla_rules_code_effective_idx");
            entry.HasIndex(row => new { row.Active, row.EffectiveFrom, row.EffectiveTo })
                .HasDatabaseName("billing_sla_rules_active_idx");
        });
    }
}
