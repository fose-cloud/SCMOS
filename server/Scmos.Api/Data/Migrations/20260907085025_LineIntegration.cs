using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class LineIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "event_at",
                table: "workflow_events",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "line_event_id",
                table: "workflow_events",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "workflow_events",
                type: "nvarchar(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "SCMOS");

            migrationBuilder.CreateTable(
                name: "line_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    webhook_event_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: ""),
                    line_message_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    line_group_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: ""),
                    line_user_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: ""),
                    message_type = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false, defaultValue: ""),
                    raw_text = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false, defaultValue: ""),
                    raw_payload = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: ""),
                    received_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    processing_status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "RECEIVED"),
                    processed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    error_code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: ""),
                    error_message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    retry_count = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    job_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false, defaultValue: ""),
                    job_number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: ""),
                    parsed_status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: ""),
                    confidence = table.Column<double>(type: "float", nullable: false, defaultValue: 0.0),
                    matched_rules = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    warnings = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_line_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "line_groups",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    line_group_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    group_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    group_type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "VENDOR"),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_line_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "line_users",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    line_user_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    staff_id = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    role = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false, defaultValue: "UNKNOWN"),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_line_users", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "line_events_group_idx",
                table: "line_events",
                columns: new[] { "line_group_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "line_events_message_idx",
                table: "line_events",
                column: "line_message_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "line_events_queue_idx",
                table: "line_events",
                columns: new[] { "processing_status", "received_at" });

            migrationBuilder.CreateIndex(
                name: "line_groups_id_idx",
                table: "line_groups",
                column: "line_group_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "line_users_id_idx",
                table: "line_users",
                column: "line_user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "line_events");

            migrationBuilder.DropTable(
                name: "line_groups");

            migrationBuilder.DropTable(
                name: "line_users");

            migrationBuilder.DropColumn(
                name: "event_at",
                table: "workflow_events");

            migrationBuilder.DropColumn(
                name: "line_event_id",
                table: "workflow_events");

            migrationBuilder.DropColumn(
                name: "source",
                table: "workflow_events");
        }
    }
}
