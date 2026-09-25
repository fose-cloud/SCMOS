using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CarrierBillingPhase5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "billing_cases_status_ck",
                table: "billing_cases");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "submitted_at",
                table: "billing_invoices",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "validated_at",
                table: "billing_invoices",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "billing_additional_charges",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    charge_type = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false, defaultValue: ""),
                    requested_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    approved_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false, defaultValue: ""),
                    reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    evidence_document_id = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: ""),
                    requested_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    requested_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    approved_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    approved_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_additional_charges", x => x.id);
                    table.ForeignKey(
                        name: "FK_billing_additional_charges_billing_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "billing_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_billing_additional_charges_documents_evidence_document_id",
                        column: x => x.evidence_document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "billing_requirement_rules",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false, defaultValue: ""),
                    document_kind = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false, defaultValue: ""),
                    customer = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, defaultValue: ""),
                    supplier_id = table.Column<int>(type: "int", nullable: true),
                    service_type = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false, defaultValue: ""),
                    shipment_type = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false, defaultValue: ""),
                    charge_type = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false, defaultValue: ""),
                    specific_requirement = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false, defaultValue: ""),
                    required = table.Column<bool>(type: "bit", nullable: false),
                    blocking = table.Column<bool>(type: "bit", nullable: false),
                    priority = table.Column<int>(type: "int", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    active = table.Column<bool>(type: "bit", nullable: false),
                    updated_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_requirement_rules", x => x.id);
                    table.ForeignKey(
                        name: "FK_billing_requirement_rules_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "billing_tax_rules",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false, defaultValue: ""),
                    tax_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: ""),
                    rate = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    customer = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, defaultValue: ""),
                    supplier_id = table.Column<int>(type: "int", nullable: true),
                    service_type = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false, defaultValue: ""),
                    shipment_type = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false, defaultValue: ""),
                    priority = table.Column<int>(type: "int", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    active = table.Column<bool>(type: "bit", nullable: false),
                    updated_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_tax_rules", x => x.id);
                    table.ForeignKey(
                        name: "FK_billing_tax_rules_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "billing_validation_runs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    submitted_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    started_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_validation_runs", x => x.id);
                    table.ForeignKey(
                        name: "FK_billing_validation_runs_billing_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "billing_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "billing_requirement_snapshots",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    rule_id = table.Column<long>(type: "bigint", nullable: false),
                    rule_code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false, defaultValue: ""),
                    document_kind = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false, defaultValue: ""),
                    required = table.Column<bool>(type: "bit", nullable: false),
                    blocking = table.Column<bool>(type: "bit", nullable: false),
                    priority = table.Column<int>(type: "int", nullable: false),
                    satisfied = table.Column<bool>(type: "bit", nullable: false),
                    document_id = table.Column<long>(type: "bigint", nullable: true),
                    snapshotted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_requirement_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "FK_billing_requirement_snapshots_billing_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "billing_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_billing_requirement_snapshots_billing_requirement_rules_rule_id",
                        column: x => x.rule_id,
                        principalTable: "billing_requirement_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_billing_requirement_snapshots_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "billing_validation_results",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    run_id = table.Column<long>(type: "bigint", nullable: false),
                    invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    step = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: ""),
                    code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false, defaultValue: ""),
                    category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    blocking = table.Column<bool>(type: "bit", nullable: false),
                    message = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: false, defaultValue: ""),
                    expected_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    actual_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false, defaultValue: ""),
                    evidence_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: ""),
                    evidence_id = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    evidence_version = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false, defaultValue: ""),
                    rule_source = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false, defaultValue: ""),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_validation_results", x => x.id);
                    table.ForeignKey(
                        name: "FK_billing_validation_results_billing_validation_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "billing_validation_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "billing_cases_status_ck",
                table: "billing_cases",
                sql: "[status] IN ('WAITING_CARRIER_SUBMISSION','DRAFT','VALIDATED','BLOCKED')");

            migrationBuilder.CreateIndex(
                name: "IX_billing_additional_charges_evidence_document_id",
                table: "billing_additional_charges",
                column: "evidence_document_id");

            migrationBuilder.CreateIndex(
                name: "IX_billing_additional_charges_invoice_id",
                table: "billing_additional_charges",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "billing_requirement_rule_code_date_idx",
                table: "billing_requirement_rules",
                columns: new[] { "code", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_requirement_rules_supplier_id",
                table: "billing_requirement_rules",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "IX_billing_requirement_snapshots_document_id",
                table: "billing_requirement_snapshots",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_billing_requirement_snapshots_invoice_id_rule_id",
                table: "billing_requirement_snapshots",
                columns: new[] { "invoice_id", "rule_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_requirement_snapshots_rule_id",
                table: "billing_requirement_snapshots",
                column: "rule_id");

            migrationBuilder.CreateIndex(
                name: "IX_billing_tax_rules_code_effective_from",
                table: "billing_tax_rules",
                columns: new[] { "code", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_tax_rules_supplier_id",
                table: "billing_tax_rules",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "IX_billing_validation_results_invoice_id_run_id_sequence",
                table: "billing_validation_results",
                columns: new[] { "invoice_id", "run_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_billing_validation_results_run_id",
                table: "billing_validation_results",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "IX_billing_validation_runs_invoice_id_sequence",
                table: "billing_validation_runs",
                columns: new[] { "invoice_id", "sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_additional_charges");

            migrationBuilder.DropTable(
                name: "billing_requirement_snapshots");

            migrationBuilder.DropTable(
                name: "billing_tax_rules");

            migrationBuilder.DropTable(
                name: "billing_validation_results");

            migrationBuilder.DropTable(
                name: "billing_requirement_rules");

            migrationBuilder.DropTable(
                name: "billing_validation_runs");

            migrationBuilder.DropCheckConstraint(
                name: "billing_cases_status_ck",
                table: "billing_cases");

            migrationBuilder.DropColumn(
                name: "submitted_at",
                table: "billing_invoices");

            migrationBuilder.DropColumn(
                name: "validated_at",
                table: "billing_invoices");

            migrationBuilder.AddCheckConstraint(
                name: "billing_cases_status_ck",
                table: "billing_cases",
                sql: "[status] IN ('WAITING_CARRIER_SUBMISSION','DRAFT')");
        }
    }
}
