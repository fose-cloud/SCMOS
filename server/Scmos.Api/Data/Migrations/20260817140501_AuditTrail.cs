using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    who = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    who_id = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    role = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false, defaultValue: ""),
                    action = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    entity = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    entity_id = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    entity_label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    field = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false, defaultValue: ""),
                    old_value = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false, defaultValue: ""),
                    new_value = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false, defaultValue: ""),
                    reason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false, defaultValue: ""),
                    ip_address = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false, defaultValue: ""),
                    session_id = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "web")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "audit_at_idx",
                table: "audit_events",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "audit_entity_idx",
                table: "audit_events",
                columns: new[] { "entity", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "audit_who_idx",
                table: "audit_events",
                column: "who");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events");
        }
    }
}
