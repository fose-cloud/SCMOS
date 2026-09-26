using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CarrierBillingPhase7 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "billing_cases_status_ck",
                table: "billing_cases");

            migrationBuilder.CreateTable(
                name: "billing_original_packages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    sent_date = table.Column<DateOnly>(type: "date", nullable: true),
                    courier = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    tracking_number = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, defaultValue: ""),
                    carrier_package_reference = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, defaultValue: ""),
                    carrier_remark = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: false, defaultValue: ""),
                    sent_by = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, defaultValue: ""),
                    sent_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    received_by_id = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, defaultValue: ""),
                    received_by_name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, defaultValue: ""),
                    document_count = table.Column<int>(type: "int", nullable: true),
                    receipt_package_reference = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, defaultValue: ""),
                    receipt_remark = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: false, defaultValue: ""),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_original_packages", x => x.id);
                    table.CheckConstraint("billing_original_packages_count_ck", "[document_count] IS NULL OR [document_count] > 0");
                    table.CheckConstraint("billing_original_packages_status_ck", "[status] IN ('PENDING','SENT','RECEIVED')");
                    table.ForeignKey(
                        name: "FK_billing_original_packages_billing_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "billing_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "billing_cases_status_ck",
                table: "billing_cases",
                sql: "[status] IN ('WAITING_CARRIER_SUBMISSION','DRAFT','VALIDATED','BLOCKED','SUBCON_REVIEW','RETURNED','DISPUTED','AWAITING_ORIGINAL','ORIGINAL_RECEIVED','READY_FOR_FINANCE')");

            migrationBuilder.CreateIndex(
                name: "billing_original_packages_invoice_idx",
                table: "billing_original_packages",
                column: "invoice_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "billing_original_packages_status_received_idx",
                table: "billing_original_packages",
                columns: new[] { "status", "received_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_original_packages");

            migrationBuilder.DropCheckConstraint(
                name: "billing_cases_status_ck",
                table: "billing_cases");

            migrationBuilder.AddCheckConstraint(
                name: "billing_cases_status_ck",
                table: "billing_cases",
                sql: "[status] IN ('WAITING_CARRIER_SUBMISSION','DRAFT','VALIDATED','BLOCKED','SUBCON_REVIEW','RETURNED','DISPUTED','AWAITING_ORIGINAL')");
        }
    }
}
