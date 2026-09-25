using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CarrierCollaborationPhase2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "previous_request_id",
                table: "supplier_requests",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reason_code",
                table: "supplier_requests",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "remark",
                table: "supplier_requests",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "responded_by",
                table: "supplier_requests",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "supplier_requests",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "supplier_id",
                table: "supplier_requests",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_requests_previous_request_id",
                table: "supplier_requests",
                column: "previous_request_id");

            // Do not guess which carrier owns a legacy conflict. Preserve all
            // rows and stop with a clear preflight error for operations to
            // resolve before the integrity constraint is installed.
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT job_key
                    FROM supplier_requests
                    WHERE outcome IN ('pending', 'confirmed')
                    GROUP BY job_key
                    HAVING COUNT(*) > 1
                )
                    THROW 51002, 'Carrier Collaboration Phase 2: a job has more than one active supplier request; resolve the preserved history before applying this migration.', 1;
                """);

            migrationBuilder.CreateIndex(
                name: "supplier_requests_one_active_job_idx",
                table: "supplier_requests",
                column: "job_key",
                unique: true,
                filter: "[outcome] IN ('pending','confirmed')");

            migrationBuilder.CreateIndex(
                name: "supplier_requests_supplier_outcome_idx",
                table: "supplier_requests",
                columns: new[] { "supplier_id", "outcome" });

            migrationBuilder.AddForeignKey(
                name: "FK_supplier_requests_previous_request",
                table: "supplier_requests",
                column: "previous_request_id",
                principalTable: "supplier_requests",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_supplier_requests_suppliers_supplier_id",
                table: "supplier_requests",
                column: "supplier_id",
                principalTable: "suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_supplier_requests_previous_request",
                table: "supplier_requests");

            migrationBuilder.DropForeignKey(
                name: "FK_supplier_requests_suppliers_supplier_id",
                table: "supplier_requests");

            migrationBuilder.DropIndex(
                name: "IX_supplier_requests_previous_request_id",
                table: "supplier_requests");

            migrationBuilder.DropIndex(
                name: "supplier_requests_one_active_job_idx",
                table: "supplier_requests");

            migrationBuilder.DropIndex(
                name: "supplier_requests_supplier_outcome_idx",
                table: "supplier_requests");

            migrationBuilder.DropColumn(
                name: "previous_request_id",
                table: "supplier_requests");

            migrationBuilder.DropColumn(
                name: "reason_code",
                table: "supplier_requests");

            migrationBuilder.DropColumn(
                name: "remark",
                table: "supplier_requests");

            migrationBuilder.DropColumn(
                name: "responded_by",
                table: "supplier_requests");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "supplier_requests");

            migrationBuilder.DropColumn(
                name: "supplier_id",
                table: "supplier_requests");
        }
    }
}
