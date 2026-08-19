using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CustomerTrainingControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_training_requirements",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    customer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    course_id = table.Column<int>(type: "int", nullable: false),
                    valid_months = table.Column<int>(type: "int", nullable: true),
                    mandatory = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    note = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false, defaultValue: ""),
                    updated_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_training_requirements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "driver_training",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    driver_id = table.Column<int>(type: "int", nullable: false),
                    course_id = table.Column<int>(type: "int", nullable: false),
                    customer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    training_date = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    expiry_date = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    certificate_no = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    provider = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    remark = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false, defaultValue: ""),
                    document_id = table.Column<long>(type: "bigint", nullable: true),
                    created_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    voided = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    void_reason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false, defaultValue: ""),
                    voided_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_driver_training", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "drivers",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    driver_id_no = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false, defaultValue: ""),
                    phone = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false, defaultValue: ""),
                    supplier_id = table.Column<int>(type: "int", nullable: true),
                    active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    note = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false, defaultValue: ""),
                    created_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: ""),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drivers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "training_courses",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    valid_months = table.Column<int>(type: "int", nullable: false, defaultValue: 12),
                    active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    note = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_training_courses", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "customer_course_idx",
                table: "customer_training_requirements",
                columns: new[] { "customer", "course_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "driver_training_expiry_idx",
                table: "driver_training",
                column: "expiry_date");

            migrationBuilder.CreateIndex(
                name: "driver_training_idx",
                table: "driver_training",
                columns: new[] { "driver_id", "course_id" });

            migrationBuilder.CreateIndex(
                name: "drivers_id_no_idx",
                table: "drivers",
                column: "driver_id_no",
                unique: true,
                filter: "[driver_id_no] <> ''");

            migrationBuilder.CreateIndex(
                name: "drivers_supplier_idx",
                table: "drivers",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "training_course_code_idx",
                table: "training_courses",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_training_requirements");

            migrationBuilder.DropTable(
                name: "driver_training");

            migrationBuilder.DropTable(
                name: "drivers");

            migrationBuilder.DropTable(
                name: "training_courses");
        }
    }
}
