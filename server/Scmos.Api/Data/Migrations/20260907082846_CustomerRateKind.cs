using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CustomerRateKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "customer_rate_lanes_idx",
                table: "customer_rate_lanes");

            migrationBuilder.DropIndex(
                name: "customer_rate_bands_idx",
                table: "customer_rate_bands");

            migrationBuilder.AddColumn<string>(
                name: "cargo_type",
                table: "customer_rate_lanes",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "customer_rate_lanes",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "COST");

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "customer_rate_bands",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "COST");

            migrationBuilder.CreateIndex(
                name: "customer_rate_lanes_idx",
                table: "customer_rate_lanes",
                columns: new[] { "customer", "carrier", "kind" });

            migrationBuilder.CreateIndex(
                name: "customer_rate_bands_idx",
                table: "customer_rate_bands",
                columns: new[] { "customer", "kind", "position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "customer_rate_lanes_idx",
                table: "customer_rate_lanes");

            migrationBuilder.DropIndex(
                name: "customer_rate_bands_idx",
                table: "customer_rate_bands");

            migrationBuilder.DropColumn(
                name: "cargo_type",
                table: "customer_rate_lanes");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "customer_rate_lanes");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "customer_rate_bands");

            migrationBuilder.CreateIndex(
                name: "customer_rate_lanes_idx",
                table: "customer_rate_lanes",
                columns: new[] { "customer", "carrier" });

            migrationBuilder.CreateIndex(
                name: "customer_rate_bands_idx",
                table: "customer_rate_bands",
                columns: new[] { "customer", "position" });
        }
    }
}
