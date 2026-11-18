using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class CounterSaleReturn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_client_account_entries_client_unless_counter_sale",
                schema: "salon",
                table: "client_account_entries");

            migrationBuilder.AddColumn<int>(
                name: "returned_quantity",
                schema: "salon",
                table: "counter_sale_lines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "ck_counter_sale_lines_returned_within_quantity",
                schema: "salon",
                table: "counter_sale_lines",
                sql: "returned_quantity >= 0 AND returned_quantity <= quantity");

            migrationBuilder.AddCheckConstraint(
                name: "ck_client_account_entries_client_unless_counter_sale",
                schema: "salon",
                table: "client_account_entries",
                sql: "client_id IS NOT NULL OR (kind IN (8, 9) AND appointment_id IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_counter_sale_lines_returned_within_quantity",
                schema: "salon",
                table: "counter_sale_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_client_account_entries_client_unless_counter_sale",
                schema: "salon",
                table: "client_account_entries");

            migrationBuilder.DropColumn(
                name: "returned_quantity",
                schema: "salon",
                table: "counter_sale_lines");

            migrationBuilder.AddCheckConstraint(
                name: "ck_client_account_entries_client_unless_counter_sale",
                schema: "salon",
                table: "client_account_entries",
                sql: "client_id IS NOT NULL OR (kind = 8 AND appointment_id IS NULL)");
        }
    }
}
