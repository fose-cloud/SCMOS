using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReportArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "report_archive",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    customer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    month = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false, defaultValue: ""),
                    taken_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    taken_by = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, defaultValue: ""),
                    document = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    trips = table.Column<int>(type: "int", nullable: false),
                    measurable = table.Column<int>(type: "int", nullable: false),
                    otd = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_archive", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "report_archive_month_idx",
                table: "report_archive",
                columns: new[] { "customer", "month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "report_archive");
        }
    }
}
