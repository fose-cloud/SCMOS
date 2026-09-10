using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MailAttachmentBytes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "fetch_attempts",
                table: "email_attachments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "fetch_error",
                table: "email_attachments",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "fetched_at",
                table: "email_attachments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "email_attachments",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "file");

            migrationBuilder.CreateIndex(
                name: "email_attachments_pending_idx",
                table: "email_attachments",
                columns: new[] { "stored_document_id", "fetch_attempts" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "email_attachments_pending_idx",
                table: "email_attachments");

            migrationBuilder.DropColumn(
                name: "fetch_attempts",
                table: "email_attachments");

            migrationBuilder.DropColumn(
                name: "fetch_error",
                table: "email_attachments");

            migrationBuilder.DropColumn(
                name: "fetched_at",
                table: "email_attachments");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "email_attachments");
        }
    }
}
