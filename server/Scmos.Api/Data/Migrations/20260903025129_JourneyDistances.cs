using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class JourneyDistances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "journey_distances",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    journey_key = table.Column<string>(type: "nvarchar(420)", maxLength: 420, nullable: false, defaultValue: ""),
                    from_place = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false, defaultValue: ""),
                    to_place = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false, defaultValue: ""),
                    km = table.Column<int>(type: "int", nullable: false),
                    set_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    set_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    used_count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journey_distances", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "journey_key_idx",
                table: "journey_distances",
                column: "journey_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "journey_distances");
        }
    }
}
