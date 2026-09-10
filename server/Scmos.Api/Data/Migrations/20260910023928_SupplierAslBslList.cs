using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SupplierAslBslList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AbsNo",
                table: "suppliers",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ContactPerson",
                table: "suppliers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CreditTerm",
                table: "suppliers",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "suppliers",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Fax",
                table: "suppliers",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsCarrier",
                table: "suppliers",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalName",
                table: "suppliers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ListType",
                table: "suppliers",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MainSpType",
                table: "suppliers",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ServicesRequired",
                table: "suppliers",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Telephone",
                table: "suppliers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TypeOfService",
                table: "suppliers",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Website",
                table: "suppliers",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "suppliers_abs_no_idx",
                table: "suppliers",
                column: "AbsNo");

            migrationBuilder.CreateIndex(
                name: "suppliers_carrier_idx",
                table: "suppliers",
                column: "IsCarrier");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "suppliers_abs_no_idx",
                table: "suppliers");

            migrationBuilder.DropIndex(
                name: "suppliers_carrier_idx",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "AbsNo",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "ContactPerson",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "CreditTerm",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "Fax",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "IsCarrier",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "LegalName",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "ListType",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "MainSpType",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "ServicesRequired",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "Telephone",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "TypeOfService",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "Website",
                table: "suppliers");
        }
    }
}
