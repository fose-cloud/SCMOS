using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BlobStorageStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "incident_evidence");

            migrationBuilder.DropTable(
                name: "supplier_documents");

            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    scope = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    job_key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    supplier_id = table.Column<int>(type: "int", nullable: true),
                    case_id = table.Column<long>(type: "bigint", nullable: true),
                    folder = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    kind = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false, defaultValue: ""),
                    year = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false, defaultValue: ""),
                    customer = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    job_ref = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    file_name = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    content_type = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, defaultValue: ""),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    object_key = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    blob_url = table.Column<string>(type: "nvarchar(700)", maxLength: 700, nullable: false, defaultValue: ""),
                    expiry_date = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    uploaded_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "document_case_idx",
                table: "documents",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "document_job_idx",
                table: "documents",
                columns: new[] { "job_key", "folder" });

            migrationBuilder.CreateIndex(
                name: "document_key_idx",
                table: "documents",
                column: "object_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "document_supplier_idx",
                table: "documents",
                column: "supplier_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "documents");

            migrationBuilder.CreateTable(
                name: "incident_evidence",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    case_id = table.Column<long>(type: "bigint", nullable: false),
                    file_name = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    object_key = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    uploaded_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incident_evidence", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "supplier_documents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExpiryDate = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ObjectKey = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false, defaultValue: ""),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UploadedBy = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_documents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "incident_evidence_case_idx",
                table: "incident_evidence",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "supplier_document_idx",
                table: "supplier_documents",
                columns: new[] { "SupplierId", "Kind" });
        }
    }
}
