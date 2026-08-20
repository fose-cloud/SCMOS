using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RateInquiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rate_inquiries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    number = table.Column<int>(type: "int", nullable: false),
                    inquired_on = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    requestor = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, defaultValue: ""),
                    requestor_id = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    customer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    fuel_band = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false, defaultValue: ""),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    created_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rate_inquiries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rate_inquiry_lanes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    inquiry_id = table.Column<long>(type: "bigint", nullable: false),
                    from_place = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false, defaultValue: ""),
                    to_place = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false, defaultValue: ""),
                    county = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    carriers = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false, defaultValue: ""),
                    fcl = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    lcl = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    remark = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rate_inquiry_lanes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rate_inquiry_prices",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    lane_id = table.Column<long>(type: "bigint", nullable: false),
                    vehicle = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    price = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rate_inquiry_prices", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "rate_inquiry_customer_idx",
                table: "rate_inquiries",
                column: "customer");

            migrationBuilder.CreateIndex(
                name: "rate_inquiry_requestor_idx",
                table: "rate_inquiries",
                columns: new[] { "requestor_id", "id" });

            migrationBuilder.CreateIndex(
                name: "rate_inquiry_lane_inquiry_idx",
                table: "rate_inquiry_lanes",
                column: "inquiry_id");

            migrationBuilder.CreateIndex(
                name: "rate_inquiry_price_lane_vehicle_idx",
                table: "rate_inquiry_prices",
                columns: new[] { "lane_id", "vehicle" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rate_inquiries");

            migrationBuilder.DropTable(
                name: "rate_inquiry_lanes");

            migrationBuilder.DropTable(
                name: "rate_inquiry_prices");
        }
    }
}
