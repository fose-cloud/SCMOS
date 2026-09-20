using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CarrierWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "carrier_webhook_deliveries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    webhook_id = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    event_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    payload = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: ""),
                    attempts = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "pending"),
                    last_status_code = table.Column<int>(type: "int", nullable: true),
                    last_error = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false, defaultValue: ""),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    delivered_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    correlation_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_carrier_webhook_deliveries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "carrier_webhooks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    supplier_id = table.Column<int>(type: "int", nullable: false),
                    client_row_id = table.Column<long>(type: "bigint", nullable: false),
                    url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    secret = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    events = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "active"),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    disabled_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    disabled_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    last_delivery_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_status_code = table.Column<int>(type: "int", nullable: true),
                    last_error = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false, defaultValue: ""),
                    failed_in_a_row = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_carrier_webhooks", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "carrier_webhook_deliveries_due_idx",
                table: "carrier_webhook_deliveries",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "carrier_webhook_deliveries_event_idx",
                table: "carrier_webhook_deliveries",
                columns: new[] { "webhook_id", "event_type", "event_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "carrier_webhooks_supplier_idx",
                table: "carrier_webhooks",
                column: "supplier_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "carrier_webhook_deliveries");

            migrationBuilder.DropTable(
                name: "carrier_webhooks");
        }
    }
}
