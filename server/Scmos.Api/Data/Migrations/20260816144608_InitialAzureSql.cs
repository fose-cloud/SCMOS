using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialAzureSql : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operation_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    owner_name = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    work_date = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    reporting_period = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    flow = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    customer = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    subcontractor = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    job_code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    container_no = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    equipment_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    plan_at = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    actual_at = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    operation_status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    validation_status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    otd_status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    remark = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    submitted_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "operation_jobs",
                columns: table => new
                {
                    key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    cat = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    owner = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    owner_id = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    work_date = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    customer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    trucker = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    job_code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false, defaultValue: ""),
                    container = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: ""),
                    status = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false, defaultValue: ""),
                    data = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    updated_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_jobs", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "operation_uploads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    upload_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    owner_name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    flow = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    submitted_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_uploads", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "report_uploads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    period = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    filename = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    object_key = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    row_count = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    issue_count = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    uploaded_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_uploads", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "operation_entries_owner_date_idx",
                table: "operation_entries",
                columns: new[] { "owner_name", "work_date" });

            migrationBuilder.CreateIndex(
                name: "operation_entries_period_flow_idx",
                table: "operation_entries",
                columns: new[] { "reporting_period", "flow" });

            migrationBuilder.CreateIndex(
                name: "operation_jobs_cat_status_idx",
                table: "operation_jobs",
                columns: new[] { "cat", "status" });

            migrationBuilder.CreateIndex(
                name: "operation_jobs_owner_id_idx",
                table: "operation_jobs",
                columns: new[] { "owner_id", "work_date" });

            migrationBuilder.CreateIndex(
                name: "operation_jobs_owner_idx",
                table: "operation_jobs",
                columns: new[] { "owner", "work_date" });

            migrationBuilder.CreateIndex(
                name: "operation_uploads_owner_idx",
                table: "operation_uploads",
                columns: new[] { "owner_name", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "report_uploads_period_idx",
                table: "report_uploads",
                columns: new[] { "period", "uploaded_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operation_entries");

            migrationBuilder.DropTable(
                name: "operation_jobs");

            migrationBuilder.DropTable(
                name: "operation_uploads");

            migrationBuilder.DropTable(
                name: "report_uploads");
        }
    }
}
