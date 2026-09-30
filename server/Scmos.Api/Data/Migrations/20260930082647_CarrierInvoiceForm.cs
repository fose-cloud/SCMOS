using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CarrierInvoiceForm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "credit_term_days",
                table: "billing_invoices",
                type: "int",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<DateOnly>(
                name: "due_date",
                table: "billing_invoices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "job_no",
                table: "billing_invoices",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "net_amount",
                table: "billing_invoices",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "payment_note",
                table: "billing_invoices",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "po_number",
                table: "billing_invoices",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "prepared_by",
                table: "billing_invoices",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "withholding_amount",
                table: "billing_invoices",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "billing_invoice_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    position = table.Column<int>(type: "int", nullable: false),
                    quantity = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    unit_price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false, defaultValue: ""),
                    detail = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_invoice_lines", x => x.id);
                    table.CheckConstraint("billing_invoice_lines_amounts_ck", "[quantity] >= 0 AND [unit_price] >= 0 AND [amount] >= 0");
                    table.ForeignKey(
                        name: "FK_billing_invoice_lines_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "billing_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "billing_invoice_lines_invoice_code_idx",
                table: "billing_invoice_lines",
                columns: new[] { "invoice_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_invoice_lines");

            migrationBuilder.DropColumn(
                name: "credit_term_days",
                table: "billing_invoices");

            migrationBuilder.DropColumn(
                name: "due_date",
                table: "billing_invoices");

            migrationBuilder.DropColumn(
                name: "job_no",
                table: "billing_invoices");

            migrationBuilder.DropColumn(
                name: "net_amount",
                table: "billing_invoices");

            migrationBuilder.DropColumn(
                name: "payment_note",
                table: "billing_invoices");

            migrationBuilder.DropColumn(
                name: "po_number",
                table: "billing_invoices");

            migrationBuilder.DropColumn(
                name: "prepared_by",
                table: "billing_invoices");

            migrationBuilder.DropColumn(
                name: "withholding_amount",
                table: "billing_invoices");
        }
    }
}
