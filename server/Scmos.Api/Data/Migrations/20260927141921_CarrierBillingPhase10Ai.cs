using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CarrierBillingPhase10Ai : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "billing_ai_analyses",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    billing_case_id = table.Column<long>(type: "bigint", nullable: true),
                    document_id = table.Column<long>(type: "bigint", nullable: true),
                    kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: ""),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    result_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false, defaultValue: ""),
                    confidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, defaultValue: ""),
                    evidence_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: ""),
                    evidence_reference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    evidence_version = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    requested_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    requested_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    decided_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    decided_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    decision_remark = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_ai_analyses", x => x.id);
                    table.CheckConstraint("billing_ai_confidence_ck", "[confidence] >= 0 AND [confidence] <= 1");
                    table.CheckConstraint("billing_ai_kind_ck", "[kind] IN ('DOCUMENT_CLASSIFICATION','INVOICE_EXTRACTION','POD_EXTRACTION','BILLING_RISK','VARIANCE_EXPLANATION')");
                    table.CheckConstraint("billing_ai_status_ck", "[status] IN ('SUGGESTED','CONFIRMED','REJECTED')");
                    table.ForeignKey(
                        name: "FK_billing_ai_analyses_billing_cases_billing_case_id",
                        column: x => x.billing_case_id,
                        principalTable: "billing_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_billing_ai_analyses_billing_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "billing_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_billing_ai_analyses_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "billing_ai_case_status_idx",
                table: "billing_ai_analyses",
                columns: new[] { "billing_case_id", "status" });

            migrationBuilder.CreateIndex(
                name: "billing_ai_invoice_requested_idx",
                table: "billing_ai_analyses",
                columns: new[] { "invoice_id", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "IX_billing_ai_analyses_document_id",
                table: "billing_ai_analyses",
                column: "document_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_ai_analyses");
        }
    }
}
