using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class IssueAttachmentsAndVehicle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "issue_id",
                table: "documents",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "document_issue_idx",
                table: "documents",
                column: "issue_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "document_issue_idx",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "issue_id",
                table: "documents");
        }
    }
}
