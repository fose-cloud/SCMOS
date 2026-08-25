using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CustomerRatesAndCargoForms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cargo_form_templates",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    customer = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    source_file = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false, defaultValue: ""),
                    columns = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cargo_form_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customer_rate_bands",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    customer = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    label = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    min_price = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    max_price = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_rate_bands", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customer_rate_lanes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    customer = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    carrier = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    from_place = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    to_place = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    postal_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_rate_lanes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customer_rate_prices",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    lane_id = table.Column<long>(type: "bigint", nullable: false),
                    vehicle = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    band_position = table.Column<int>(type: "int", nullable: false),
                    price = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_rate_prices", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "cargo_form_templates_customer_idx",
                table: "cargo_form_templates",
                column: "customer",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "customer_rate_bands_idx",
                table: "customer_rate_bands",
                columns: new[] { "customer", "position" });

            migrationBuilder.CreateIndex(
                name: "customer_rate_lanes_idx",
                table: "customer_rate_lanes",
                columns: new[] { "customer", "carrier" });

            migrationBuilder.CreateIndex(
                name: "customer_rate_prices_lane_idx",
                table: "customer_rate_prices",
                column: "lane_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cargo_form_templates");

            migrationBuilder.DropTable(
                name: "customer_rate_bands");

            migrationBuilder.DropTable(
                name: "customer_rate_lanes");

            migrationBuilder.DropTable(
                name: "customer_rate_prices");
        }
    }
}
