using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PromoteQuotedRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "FromInquiryLaneId",
                table: "rate_lanes",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PromotedAt",
                table: "rate_lanes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromotedBy",
                table: "rate_lanes",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "rate_lane_from_inquiry_idx",
                table: "rate_lanes",
                column: "FromInquiryLaneId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "rate_lane_from_inquiry_idx",
                table: "rate_lanes");

            migrationBuilder.DropColumn(
                name: "FromInquiryLaneId",
                table: "rate_lanes");

            migrationBuilder.DropColumn(
                name: "PromotedAt",
                table: "rate_lanes");

            migrationBuilder.DropColumn(
                name: "PromotedBy",
                table: "rate_lanes");
        }
    }
}
