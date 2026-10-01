using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scmos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class TrainingRecordSupplier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "supplier_id",
                table: "customer_training_records",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "customer_training_supplier_idx",
                table: "customer_training_records",
                column: "supplier_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "customer_training_supplier_idx",
                table: "customer_training_records");

            migrationBuilder.DropColumn(
                name: "supplier_id",
                table: "customer_training_records");
        }
    }
}
