using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class OperationsAiControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_operations_control",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false),
                    enabled = table.Column<bool>(type: "bit", nullable: false),
                    revision = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_operations_control", x => x.id);
                    table.CheckConstraint("CK_ai_operations_control_singleton", "[id] = 1");
                });

            migrationBuilder.InsertData(
                table: "ai_operations_control",
                columns: new[] { "id", "enabled", "revision" },
                values: new object[] { 1, false, 0 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_operations_control");
        }
    }
}
