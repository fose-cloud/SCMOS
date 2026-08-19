using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DriverPhotoAndDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "photo_document_id",
                table: "drivers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "driver_id",
                table: "documents",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "photo_document_id",
                table: "drivers");

            migrationBuilder.DropColumn(
                name: "driver_id",
                table: "documents");
        }
    }
}
