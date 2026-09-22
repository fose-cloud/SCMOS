using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class JobCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_corrections",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    batch = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    job_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    job_code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false, defaultValue: ""),
                    owner_id = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    field = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    from_value = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    to_value = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    rule = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false, defaultValue: ""),
                    proposed_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    proposed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    state = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false, defaultValue: "pending"),
                    decided_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    decided_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_corrections", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "job_corrections_pending_cell_idx",
                table: "job_corrections",
                columns: new[] { "job_key", "field" },
                unique: true,
                filter: "[state] = 'pending'");

            migrationBuilder.CreateIndex(
                name: "job_corrections_state_idx",
                table: "job_corrections",
                columns: new[] { "state", "job_key" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_corrections");
        }
    }
}
