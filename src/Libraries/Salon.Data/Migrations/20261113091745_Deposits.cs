using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class Deposits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "deposit_percent",
                schema: "salon",
                table: "services",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "deposit_percent",
                schema: "salon",
                table: "clients",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "deposit_amount",
                schema: "salon",
                table: "appointments",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "ux_client_account_entries_one_deposit_per_appointment",
                schema: "salon",
                table: "client_account_entries",
                columns: new[] { "tenant_id", "appointment_id" },
                unique: true,
                filter: "kind = 5");

            migrationBuilder.CreateIndex(
                name: "ux_client_account_entries_one_return_per_deposit",
                schema: "salon",
                table: "client_account_entries",
                columns: new[] { "tenant_id", "related_entry_id" },
                unique: true,
                filter: "kind = 6");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_client_account_entries_one_deposit_per_appointment",
                schema: "salon",
                table: "client_account_entries");

            migrationBuilder.DropIndex(
                name: "ux_client_account_entries_one_return_per_deposit",
                schema: "salon",
                table: "client_account_entries");

            migrationBuilder.DropColumn(
                name: "deposit_percent",
                schema: "salon",
                table: "services");

            migrationBuilder.DropColumn(
                name: "deposit_percent",
                schema: "salon",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "deposit_amount",
                schema: "salon",
                table: "appointments");
        }
    }
}
