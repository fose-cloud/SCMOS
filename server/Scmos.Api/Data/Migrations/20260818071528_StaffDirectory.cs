using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class StaffDirectory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "staff",
                columns: table => new
                {
                    id = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    account = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false, defaultValue: ""),
                    role = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false, defaultValue: ""),
                    created_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "staff_account_idx",
                table: "staff",
                column: "account");

            migrationBuilder.CreateIndex(
                name: "staff_email_idx",
                table: "staff",
                column: "email",
                unique: true,
                filter: "[email] <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "staff");
        }
    }
}
