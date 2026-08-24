using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class IncidentCarParForm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "approval_note",
                table: "incident_cases",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "approval_outcome",
                table: "incident_cases",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "company",
                table: "incident_cases",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "documents_to_revise",
                table: "incident_cases",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "follow_up_by",
                table: "incident_cases",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "grade",
                table: "incident_cases",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "immediate_action",
                table: "incident_cases",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "immediate_by",
                table: "incident_cases",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "immediate_due",
                table: "incident_cases",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "nc_clause",
                table: "incident_cases",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "requested_by",
                table: "incident_cases",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "requested_on",
                table: "incident_cases",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "reviewed_by",
                table: "incident_cases",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "incident_cases",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "team",
                table: "incident_cases",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "team_note",
                table: "incident_cases",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "approval_note",
                table: "incident_cases");

            migrationBuilder.DropColumn(
                name: "approval_outcome",
                table: "incident_cases");

            migrationBuilder.DropColumn(
                name: "company",
                table: "incident_cases");

            migrationBuilder.DropColumn(
                name: "documents_to_revise",
                table: "incident_cases");

            migrationBuilder.DropColumn(
                name: "follow_up_by",
                table: "incident_cases");

            migrationBuilder.DropColumn(
                name: "grade",
                table: "incident_cases");

            migrationBuilder.DropColumn(
                name: "immediate_action",
                table: "incident_cases");

            migrationBuilder.DropColumn(
                name: "immediate_by",
                table: "incident_cases");

            migrationBuilder.DropColumn(
                name: "immediate_due",
                table: "incident_cases");

            migrationBuilder.DropColumn(
                name: "nc_clause",
                table: "incident_cases");

            migrationBuilder.DropColumn(
                name: "requested_by",
                table: "incident_cases");

            migrationBuilder.DropColumn(
                name: "requested_on",
                table: "incident_cases");

            migrationBuilder.DropColumn(
                name: "reviewed_by",
                table: "incident_cases");

            migrationBuilder.DropColumn(
                name: "source",
                table: "incident_cases");

            migrationBuilder.DropColumn(
                name: "team",
                table: "incident_cases");

            migrationBuilder.DropColumn(
                name: "team_note",
                table: "incident_cases");
        }
    }
}
