using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CarrierBillingPhase6 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "billing_cases_status_ck",
                table: "billing_cases");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "online_approved_at",
                table: "billing_invoices",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "review_cycle",
                table: "billing_invoices",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "review_decided_at",
                table: "billing_invoices",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "review_submitted_at",
                table: "billing_invoices",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "billing_review_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    cycle = table.Column<int>(type: "int", nullable: false),
                    action = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: ""),
                    from_status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: ""),
                    to_status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: ""),
                    reason_code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false, defaultValue: ""),
                    remark = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: false, defaultValue: ""),
                    actor_id = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, defaultValue: ""),
                    actor_name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, defaultValue: ""),
                    at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_review_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_billing_review_events_billing_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "billing_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "billing_cases_status_ck",
                table: "billing_cases",
                sql: "[status] IN ('WAITING_CARRIER_SUBMISSION','DRAFT','VALIDATED','BLOCKED','SUBCON_REVIEW','RETURNED','DISPUTED','AWAITING_ORIGINAL')");

            migrationBuilder.CreateIndex(
                name: "IX_billing_review_events_invoice_id_id",
                table: "billing_review_events",
                columns: new[] { "invoice_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_review_events");

            migrationBuilder.DropCheckConstraint(
                name: "billing_cases_status_ck",
                table: "billing_cases");

            migrationBuilder.DropColumn(
                name: "online_approved_at",
                table: "billing_invoices");

            migrationBuilder.DropColumn(
                name: "review_cycle",
                table: "billing_invoices");

            migrationBuilder.DropColumn(
                name: "review_decided_at",
                table: "billing_invoices");

            migrationBuilder.DropColumn(
                name: "review_submitted_at",
                table: "billing_invoices");

            migrationBuilder.AddCheckConstraint(
                name: "billing_cases_status_ck",
                table: "billing_cases",
                sql: "[status] IN ('WAITING_CARRIER_SUBMISSION','DRAFT','VALIDATED','BLOCKED')");
        }
    }
}
