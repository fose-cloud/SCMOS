using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CarrierJobRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "carrier_job_requests",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    supplier_id = table.Column<int>(type: "int", nullable: false),
                    supplier_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    fields = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_by = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    decided_by = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    decision_note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    job_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    revision = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_carrier_job_requests", x => x.id);
                    table.CheckConstraint("carrier_job_requests_status_ck", "[status] IN ('PENDING','APPROVED','REJECTED','WITHDRAWN')");
                });

            migrationBuilder.CreateIndex(
                name: "carrier_job_requests_status_idx",
                table: "carrier_job_requests",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "carrier_job_requests_supplier_idx",
                table: "carrier_job_requests",
                columns: new[] { "supplier_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward-only: the table is the carriers' record of what they asked for and what they were
            // told. To stop the feature, stop using it; the rows stay.
            migrationBuilder.Sql("THROW 51000, 'Carrier job requests rollback is forward-only. Keep the table and its rows.', 1;");
        }
    }
}
