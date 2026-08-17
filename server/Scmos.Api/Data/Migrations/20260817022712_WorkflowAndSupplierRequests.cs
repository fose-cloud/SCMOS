using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowAndSupplierRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supplier_requests",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    job_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    rank = table.Column<int>(type: "int", nullable: false),
                    carrier = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    quoted_price = table.Column<int>(type: "int", nullable: true),
                    outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "pending"),
                    reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false, defaultValue: ""),
                    requested_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    responded_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    job_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    kind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    from_stage = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    to_stage = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    hold = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: ""),
                    note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    by_user = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "supplier_requests_carrier_idx",
                table: "supplier_requests",
                columns: new[] { "carrier", "outcome" });

            migrationBuilder.CreateIndex(
                name: "supplier_requests_job_idx",
                table: "supplier_requests",
                columns: new[] { "job_key", "rank" });

            migrationBuilder.CreateIndex(
                name: "workflow_events_job_idx",
                table: "workflow_events",
                columns: new[] { "job_key", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supplier_requests");

            migrationBuilder.DropTable(
                name: "workflow_events");
        }
    }
}
