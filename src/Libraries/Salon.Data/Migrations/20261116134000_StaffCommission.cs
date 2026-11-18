using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class StaffCommission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "product_commission_percent",
                schema: "salon",
                table: "staff_members",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "service_commission_percent",
                schema: "salon",
                table: "staff_members",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_staff_members_product_commission_percent",
                schema: "salon",
                table: "staff_members",
                sql: "product_commission_percent IS NULL OR (product_commission_percent >= 0 AND product_commission_percent <= 100)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_staff_members_service_commission_percent",
                schema: "salon",
                table: "staff_members",
                sql: "service_commission_percent IS NULL OR (service_commission_percent >= 0 AND service_commission_percent <= 100)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_staff_members_product_commission_percent",
                schema: "salon",
                table: "staff_members");

            migrationBuilder.DropCheckConstraint(
                name: "ck_staff_members_service_commission_percent",
                schema: "salon",
                table: "staff_members");

            migrationBuilder.DropColumn(
                name: "product_commission_percent",
                schema: "salon",
                table: "staff_members");

            migrationBuilder.DropColumn(
                name: "service_commission_percent",
                schema: "salon",
                table: "staff_members");
        }
    }
}
