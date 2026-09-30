using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FleetRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "supplier_trucks",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "supplier_trucks",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "supplier_trucks",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "head");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "supplier_drivers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "supplier_drivers",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "fleet_driver_id",
                table: "documents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "truck_id",
                table: "documents",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "document_fleet_driver_idx",
                table: "documents",
                column: "fleet_driver_id");

            migrationBuilder.CreateIndex(
                name: "document_truck_idx",
                table: "documents",
                column: "truck_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "document_fleet_driver_idx",
                table: "documents");

            migrationBuilder.DropIndex(
                name: "document_truck_idx",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "supplier_trucks");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "supplier_trucks");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "supplier_trucks");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "supplier_drivers");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "supplier_drivers");

            migrationBuilder.DropColumn(
                name: "fleet_driver_id",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "truck_id",
                table: "documents");
        }
    }
}
