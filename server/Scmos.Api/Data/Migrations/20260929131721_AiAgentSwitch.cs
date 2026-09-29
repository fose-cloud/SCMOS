using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AiAgentSwitch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "enabled",
                table: "ai_agent_settings",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "pass_enabled",
                table: "ai_agent_settings",
                type: "bit",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Unlike the other AI platform migrations this one may go back: the columns hold only the Control
            // Tower's switches, every agent then follows its flag in configuration again, and each switch made
            // stays in audit_events.
            migrationBuilder.DropColumn(
                name: "enabled",
                table: "ai_agent_settings");

            migrationBuilder.DropColumn(
                name: "pass_enabled",
                table: "ai_agent_settings");
        }
    }
}
