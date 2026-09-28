using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AiAgentGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "prompt_version",
                table: "ai_audit_logs",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ai_agent_settings",
                columns: table => new
                {
                    agent_id = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    autonomy = table.Column<int>(type: "int", nullable: false),
                    shadow_mode = table.Column<bool>(type: "bit", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    revision = table.Column<int>(type: "int", nullable: false),
                    updated_by = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_agent_settings", x => x.agent_id);
                    table.CheckConstraint("ai_agent_settings_autonomy_ck", "[autonomy] BETWEEN 0 AND 4");
                    table.CheckConstraint("ai_agent_settings_status_ck", "[status] IN ('ACTIVE','PAUSED','MAINTENANCE','DISABLED')");
                });

            migrationBuilder.CreateTable(
                name: "ai_decisions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    run_id = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    agent_id = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    decision_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    entity_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    entity_id = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    owner_id = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    summary = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    result_status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    risk_level = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    confidence = table.Column<decimal>(type: "decimal(4,3)", precision: 4, scale: 3, nullable: true),
                    requires_approval = table.Column<bool>(type: "bit", nullable: false),
                    shadow = table.Column<bool>(type: "bit", nullable: false),
                    autonomy = table.Column<int>(type: "int", nullable: false),
                    prompt_version = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    rule_references = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    evidence_references = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    human_choice = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    human_matches = table.Column<bool>(type: "bit", nullable: true),
                    override_reason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    decided_by_id = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    decided_by = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_decisions", x => x.id);
                    table.CheckConstraint("ai_decisions_confidence_ck", "[confidence] IS NULL OR ([confidence] >= 0 AND [confidence] <= 1)");
                    table.CheckConstraint("ai_decisions_status_ck", "[status] IN ('OPEN','ACCEPTED','OVERRIDDEN','DISMISSED')");
                });

            migrationBuilder.CreateIndex(
                name: "ai_decisions_agent_idx",
                table: "ai_decisions",
                columns: new[] { "agent_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ai_decisions_entity_idx",
                table: "ai_decisions",
                columns: new[] { "entity_type", "entity_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ai_decisions_open_idx",
                table: "ai_decisions",
                columns: new[] { "status", "owner_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward-only, as the AI audit's own migration is: the decision log and the prompt
            // version on each run are evidence, and an automatic downgrade must not erase them.
            // Older code runs against the added table and column unchanged — roll back the
            // application and switch the AI off; keep the rows.
            migrationBuilder.Sql("THROW 51000, 'AI governance rollback is forward-only. Disable AI and retain its decisions and audit.', 1;");
        }
    }
}
