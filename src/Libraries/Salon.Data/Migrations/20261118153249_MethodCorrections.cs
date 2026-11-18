using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class MethodCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_client_account_entries_client_unless_counter_sale",
                schema: "salon",
                table: "client_account_entries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_client_account_entries_client_unless_counter_sale",
                schema: "salon",
                table: "client_account_entries",
                sql: "client_id IS NOT NULL OR (kind IN (8, 9, 10, 12, 14, 15) AND appointment_id IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_client_account_entries_client_unless_counter_sale",
                schema: "salon",
                table: "client_account_entries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_client_account_entries_client_unless_counter_sale",
                schema: "salon",
                table: "client_account_entries",
                sql: "client_id IS NOT NULL OR (kind IN (8, 9, 10, 12) AND appointment_id IS NULL)");
        }
    }
}
