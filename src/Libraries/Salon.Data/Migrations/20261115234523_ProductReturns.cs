using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProductReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "returned_quantity",
                schema: "salon",
                table: "visit_product_lines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "ck_visit_product_lines_returned_within_quantity",
                schema: "salon",
                table: "visit_product_lines",
                sql: "returned_quantity >= 0 AND returned_quantity <= quantity");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_visit_product_lines_returned_within_quantity",
                schema: "salon",
                table: "visit_product_lines");

            migrationBuilder.DropColumn(
                name: "returned_quantity",
                schema: "salon",
                table: "visit_product_lines");
        }
    }
}
