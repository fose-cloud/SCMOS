using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CarrierBillingPhase11Finance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "billing_cases_status_ck",
                table: "billing_cases");

            migrationBuilder.CreateTable(
                name: "billing_finance_records",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false, defaultValue: "QUEUED"),
                    adapter = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false, defaultValue: ""),
                    idempotency_key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    payload_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    payload_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: ""),
                    attempts = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    external_reference = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, defaultValue: ""),
                    response_code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false, defaultValue: ""),
                    response_message = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: false, defaultValue: ""),
                    payment_reference = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, defaultValue: ""),
                    paid_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    reconciled_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    reconciled_by = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, defaultValue: ""),
                    created_by = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, defaultValue: ""),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_finance_records", x => x.id);
                    table.CheckConstraint("billing_finance_records_status_ck", "[status] IN ('QUEUED','PROCESSING','RETRYING','SUBMITTED','ACCEPTED','REJECTED','PAID','CLOSED','FAILED')");
                    table.ForeignKey(
                        name: "FK_billing_finance_records_billing_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "billing_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "integration_outbox",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    event_type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false, defaultValue: ""),
                    aggregate_type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false, defaultValue: ""),
                    aggregate_id = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    idempotency_key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    payload_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "PENDING"),
                    attempts = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_error = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: false, defaultValue: ""),
                    correlation_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: ""),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_outbox", x => x.id);
                    table.CheckConstraint("integration_outbox_status_ck", "[status] IN ('PENDING','PROCESSING','RETRY','COMPLETED','DEAD')");
                });

            migrationBuilder.AddCheckConstraint(
                name: "billing_cases_status_ck",
                table: "billing_cases",
                sql: "[status] IN ('WAITING_CARRIER_SUBMISSION','DRAFT','VALIDATED','BLOCKED','SUBCON_REVIEW','RETURNED','DISPUTED','AWAITING_ORIGINAL','ORIGINAL_RECEIVED','READY_FOR_FINANCE','FINANCE_PROCESSING','FINANCE_REJECTED','PAID','CLOSED')");

            migrationBuilder.CreateIndex(
                name: "billing_finance_records_idempotency_idx",
                table: "billing_finance_records",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "billing_finance_records_invoice_idx",
                table: "billing_finance_records",
                column: "invoice_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "billing_finance_records_status_due_idx",
                table: "billing_finance_records",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "integration_outbox_aggregate_idx",
                table: "integration_outbox",
                columns: new[] { "aggregate_type", "aggregate_id" });

            migrationBuilder.CreateIndex(
                name: "integration_outbox_due_idx",
                table: "integration_outbox",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "integration_outbox_idempotency_idx",
                table: "integration_outbox",
                column: "idempotency_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_finance_records");

            migrationBuilder.DropTable(
                name: "integration_outbox");

            migrationBuilder.DropCheckConstraint(
                name: "billing_cases_status_ck",
                table: "billing_cases");

            migrationBuilder.AddCheckConstraint(
                name: "billing_cases_status_ck",
                table: "billing_cases",
                sql: "[status] IN ('WAITING_CARRIER_SUBMISSION','DRAFT','VALIDATED','BLOCKED','SUBCON_REVIEW','RETURNED','DISPUTED','AWAITING_ORIGINAL','ORIGINAL_RECEIVED','READY_FOR_FINANCE')");
        }
    }
}
