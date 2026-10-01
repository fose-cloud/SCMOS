using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class IssueCaseLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "case_id",
                table: "operational_issues",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "operational_issue_case_idx",
                table: "operational_issues",
                column: "case_id");

            migrationBuilder.AddForeignKey(
                name: "FK_operational_issues_incident_cases_case_id",
                table: "operational_issues",
                column: "case_id",
                principalTable: "incident_cases",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_operational_issues_incident_cases_case_id",
                table: "operational_issues");

            migrationBuilder.DropIndex(
                name: "operational_issue_case_idx",
                table: "operational_issues");

            migrationBuilder.DropColumn(
                name: "case_id",
                table: "operational_issues");
        }
    }
}
