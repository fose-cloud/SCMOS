using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class IssueScorecardColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "scorecard_column",
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
                name: "scorecard_column",
                table: "operational_issues");
        }
    }
}
