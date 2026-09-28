using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AiDecisionLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ai_decisions_status_ck",
                table: "ai_decisions");

            migrationBuilder.AddColumn<string>(
                name: "fingerprint",
                table: "ai_decisions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddCheckConstraint(
                name: "ai_decisions_status_ck",
                table: "ai_decisions",
                sql: "[status] IN ('OPEN','ACCEPTED','OVERRIDDEN','DISMISSED','SUPERSEDED','RESOLVED')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward-only, as AiAgentGovernance is: once a pass has superseded or resolved a decision,
            // the old constraint cannot be put back without erasing that history.
            migrationBuilder.Sql("THROW 51000, 'AI decision lifecycle rollback is forward-only. Disable the agents and retain the decisions.', 1;");
        }
    }
}
