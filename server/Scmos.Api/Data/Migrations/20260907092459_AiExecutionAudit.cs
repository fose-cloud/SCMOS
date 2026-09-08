using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AiExecutionAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_audit_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    run_id = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    user_id = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    user_role = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    agent_id = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    @event = table.Column<string>(name: "event", type: "nvarchar(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    team_scope = table.Column<bool>(type: "bit", nullable: false),
                    operator_id = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    tool_name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    tool_call_id = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    view = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    result_limit = table.Column<int>(type: "int", nullable: true),
                    total = table.Column<int>(type: "int", nullable: true),
                    returned = table.Column<int>(type: "int", nullable: true),
                    input_tokens = table.Column<int>(type: "int", nullable: true),
                    output_tokens = table.Column<int>(type: "int", nullable: true),
                    source_keys = table.Column<string>(type: "nvarchar(max)", maxLength: 6000, nullable: false),
                    risk = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    approval_status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_audit_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ai_audit_logs_at_idx",
                table: "ai_audit_logs",
                columns: new[] { "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ai_audit_logs_run_sequence_idx",
                table: "ai_audit_logs",
                columns: new[] { "run_id", "sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Roll back application/flags, never erase evidence through an automatic downgrade.
            migrationBuilder.Sql("THROW 51000, 'AI audit rollback is forward-only. Disable AI and retain audit evidence.', 1;");
        }
    }
}
