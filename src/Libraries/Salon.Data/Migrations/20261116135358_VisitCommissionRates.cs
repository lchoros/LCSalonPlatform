using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class VisitCommissionRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "commission_rates_kept",
                schema: "salon",
                table: "appointments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "product_commission_percent",
                schema: "salon",
                table: "appointments",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "service_commission_percent",
                schema: "salon",
                table: "appointments",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "commission_rates_kept",
                schema: "salon",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "product_commission_percent",
                schema: "salon",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "service_commission_percent",
                schema: "salon",
                table: "appointments");
        }
    }
}
