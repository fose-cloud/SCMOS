using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AiPermissionAuthorizationAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_authorization_logs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AgentId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PolicyVersion = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    SecurityEvent = table.Column<bool>(type: "bit", nullable: false),
                    ReservedCost = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Metadata = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_authorization_logs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_authorization_logs_AgentId_At",
                table: "ai_authorization_logs",
                columns: new[] { "AgentId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_ai_authorization_logs_SecurityEvent_At",
                table: "ai_authorization_logs",
                columns: new[] { "SecurityEvent", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("THROW 51000, 'AI authorization audit rollback is forward-only. Disable AI and retain security evidence.', 1;");
        }
    }
}
