using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class OperationalIssues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operational_issues",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    found_on = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    found_at = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: ""),
                    source = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false, defaultValue: ""),
                    reporter = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, defaultValue: ""),
                    job_ref = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    job_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false, defaultValue: ""),
                    detail = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: ""),
                    category = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false, defaultValue: ""),
                    severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    impact = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: ""),
                    channel = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false, defaultValue: ""),
                    owner = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, defaultValue: ""),
                    owner_id = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    due_on = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: ""),
                    root_cause = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    created_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operational_issues", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "operational_issue_code_idx",
                table: "operational_issues",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "operational_issue_job_idx",
                table: "operational_issues",
                column: "job_key");

            migrationBuilder.CreateIndex(
                name: "operational_issue_status_idx",
                table: "operational_issues",
                columns: new[] { "status", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operational_issues");
        }
    }
}
