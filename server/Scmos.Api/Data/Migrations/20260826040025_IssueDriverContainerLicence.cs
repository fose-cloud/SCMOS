using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class IssueDriverContainerLicence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "container_no",
                table: "operational_issues",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "driver",
                table: "operational_issues",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "licence",
                table: "operational_issues",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "container_no",
                table: "operational_issues");

            migrationBuilder.DropColumn(
                name: "driver",
                table: "operational_issues");

            migrationBuilder.DropColumn(
                name: "licence",
                table: "operational_issues");
        }
    }
}
