using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CarrierBillingFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "billing_sla_rules",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    start_day = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    target_working_days = table.Column<int>(type: "int", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    updated_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_sla_rules", x => x.id);
                    table.CheckConstraint("billing_sla_rules_dates_ck", "[effective_to] IS NULL OR [effective_to] >= [effective_from]");
                    table.CheckConstraint("billing_sla_rules_start_day_ck", "[start_day] IN ('day0','day1')");
                    table.CheckConstraint("billing_sla_rules_target_ck", "[target_working_days] IN (3,4)");
                });

            migrationBuilder.CreateTable(
                name: "business_calendar_days",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, defaultValue: ""),
                    updated_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_calendar_days", x => x.date);
                    table.CheckConstraint("business_calendar_days_kind_ck", "[kind] IN ('working-day','weekend','public-holiday','company-holiday','special-working-day')");
                });

            migrationBuilder.CreateIndex(
                name: "billing_sla_rules_active_idx",
                table: "billing_sla_rules",
                columns: new[] { "active", "effective_from", "effective_to" });

            migrationBuilder.CreateIndex(
                name: "billing_sla_rules_code_effective_idx",
                table: "billing_sla_rules",
                columns: new[] { "code", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "business_calendar_days_kind_idx",
                table: "business_calendar_days",
                column: "kind");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_sla_rules");

            migrationBuilder.DropTable(
                name: "business_calendar_days");
        }
    }
}
