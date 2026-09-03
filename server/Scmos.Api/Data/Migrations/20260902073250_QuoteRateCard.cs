using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class QuoteRateCard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "quote_extras",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    label = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    basis = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "flat"),
                    rate = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quote_extras", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quote_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    margin_percent = table.Column<decimal>(type: "decimal(6,3)", precision: 6, scale: 3, nullable: false, defaultValue: 10m),
                    updated_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quote_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quote_vehicle_rates",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    label = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false, defaultValue: ""),
                    per_km = table.Column<int>(type: "int", nullable: false),
                    base_charge = table.Column<int>(type: "int", nullable: false),
                    chill = table.Column<decimal>(type: "decimal(6,3)", precision: 6, scale: 3, nullable: false, defaultValue: 1m),
                    dangerous_goods = table.Column<int>(type: "int", nullable: false),
                    position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quote_vehicle_rates", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "quote_vehicle_code_idx",
                table: "quote_vehicle_rates",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quote_extras");

            migrationBuilder.DropTable(
                name: "quote_settings");

            migrationBuilder.DropTable(
                name: "quote_vehicle_rates");
        }
    }
}
