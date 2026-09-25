using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CarrierBillingPhase4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "billing_case_id",
                table: "documents",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "billing_invoice_id",
                table: "documents",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "billing_cases",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    job_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    supplier_id = table.Column<int>(type: "int", nullable: false),
                    assignment_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    delivery_completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    sla_rule_code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: ""),
                    sla_rule_id = table.Column<long>(type: "bigint", nullable: true),
                    sla_start_day = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false, defaultValue: ""),
                    sla_target_working_days = table.Column<int>(type: "int", nullable: true),
                    sla_start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    sla_due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    sla_issue_code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false, defaultValue: ""),
                    sla_issue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    created_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_cases", x => x.id);
                    table.CheckConstraint("billing_cases_status_ck", "[status] IN ('WAITING_CARRIER_SUBMISSION','DRAFT')");
                    table.ForeignKey(
                        name: "FK_billing_cases_billing_sla_rules_sla_rule_id",
                        column: x => x.sla_rule_id,
                        principalTable: "billing_sla_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_billing_cases_operation_jobs_job_key",
                        column: x => x.job_key,
                        principalTable: "operation_jobs",
                        principalColumn: "key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_billing_cases_supplier_requests_assignment_id",
                        column: x => x.assignment_id,
                        principalTable: "supplier_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_billing_cases_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "billing_invoices",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    supplier_id = table.Column<int>(type: "int", nullable: false),
                    invoice_number = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false, defaultValue: ""),
                    invoice_date = table.Column<DateOnly>(type: "date", nullable: true),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false, defaultValue: "THB"),
                    subtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    total_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_invoices", x => x.id);
                    table.CheckConstraint("billing_invoices_amounts_ck", "[subtotal] >= 0 AND [tax_amount] >= 0 AND [total_amount] >= 0");
                    table.ForeignKey(
                        name: "FK_billing_invoices_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "billing_invoice_job_links",
                columns: table => new
                {
                    invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    billing_case_id = table.Column<long>(type: "bigint", nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    linked_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_invoice_job_links", x => new { x.invoice_id, x.billing_case_id });
                    table.ForeignKey(
                        name: "FK_billing_invoice_job_links_cases_billing_case_id",
                        column: x => x.billing_case_id,
                        principalTable: "billing_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_billing_invoice_job_links_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "billing_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "document_billing_case_idx",
                table: "documents",
                column: "billing_case_id");

            migrationBuilder.CreateIndex(
                name: "document_billing_invoice_idx",
                table: "documents",
                column: "billing_invoice_id");

            migrationBuilder.CreateIndex(
                name: "billing_cases_due_status_idx",
                table: "billing_cases",
                columns: new[] { "sla_due_date", "status" });

            migrationBuilder.CreateIndex(
                name: "billing_cases_job_idx",
                table: "billing_cases",
                column: "job_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "billing_cases_supplier_status_idx",
                table: "billing_cases",
                columns: new[] { "supplier_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_billing_cases_assignment_id",
                table: "billing_cases",
                column: "assignment_id");

            migrationBuilder.CreateIndex(
                name: "IX_billing_cases_sla_rule_id",
                table: "billing_cases",
                column: "sla_rule_id");

            migrationBuilder.CreateIndex(
                name: "billing_invoice_job_links_case_idx",
                table: "billing_invoice_job_links",
                column: "billing_case_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "billing_invoices_supplier_number_idx",
                table: "billing_invoices",
                columns: new[] { "supplier_id", "invoice_number" },
                unique: true,
                filter: "[invoice_number] <> ''");

            migrationBuilder.CreateIndex(
                name: "billing_invoices_supplier_status_idx",
                table: "billing_invoices",
                columns: new[] { "supplier_id", "status" });

            migrationBuilder.AddForeignKey(
                name: "FK_documents_billing_cases_billing_case_id",
                table: "documents",
                column: "billing_case_id",
                principalTable: "billing_cases",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_documents_billing_invoices_billing_invoice_id",
                table: "documents",
                column: "billing_invoice_id",
                principalTable: "billing_invoices",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_documents_billing_cases_billing_case_id",
                table: "documents");

            migrationBuilder.DropForeignKey(
                name: "FK_documents_billing_invoices_billing_invoice_id",
                table: "documents");

            migrationBuilder.DropTable(
                name: "billing_invoice_job_links");

            migrationBuilder.DropTable(
                name: "billing_cases");

            migrationBuilder.DropTable(
                name: "billing_invoices");

            migrationBuilder.DropIndex(
                name: "document_billing_case_idx",
                table: "documents");

            migrationBuilder.DropIndex(
                name: "document_billing_invoice_idx",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "billing_case_id",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "billing_invoice_id",
                table: "documents");
        }
    }
}
