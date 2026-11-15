using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProductsOnTheBill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "appointment_id",
                schema: "salon",
                table: "stock_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "visit_product_lines",
                schema: "salon",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    added_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    added_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_entry_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visit_product_lines", x => x.id);
                    table.CheckConstraint("ck_visit_product_lines_quantity_positive", "quantity > 0");
                    table.CheckConstraint("ck_visit_product_lines_unit_price_not_negative", "unit_price >= 0");
                    table.ForeignKey(
                        name: "fk_visit_product_lines_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalSchema: "salon",
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_visit_product_lines_client_account_entries_payment_entry_id",
                        column: x => x.payment_entry_id,
                        principalSchema: "salon",
                        principalTable: "client_account_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_visit_product_lines_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "salon",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_visit_product_lines_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "salon",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_appointment_id",
                schema: "salon",
                table: "stock_movements",
                column: "appointment_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_product_lines_appointment_id",
                schema: "salon",
                table: "visit_product_lines",
                column: "appointment_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_product_lines_payment_entry_id",
                schema: "salon",
                table: "visit_product_lines",
                column: "payment_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_product_lines_product_id",
                schema: "salon",
                table: "visit_product_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_product_lines_tenant_id",
                schema: "salon",
                table: "visit_product_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_product_lines_tenant_id_appointment_id_added_at",
                schema: "salon",
                table: "visit_product_lines",
                columns: new[] { "tenant_id", "appointment_id", "added_at" });

            migrationBuilder.CreateIndex(
                name: "ux_visit_product_lines_one_unpaid_line_per_product",
                schema: "salon",
                table: "visit_product_lines",
                columns: new[] { "tenant_id", "appointment_id", "product_id" },
                unique: true,
                filter: "payment_entry_id IS NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_stock_movements_appointments_appointment_id",
                schema: "salon",
                table: "stock_movements",
                column: "appointment_id",
                principalSchema: "salon",
                principalTable: "appointments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            RowLevelSecurity.Enable(migrationBuilder, "visit_product_lines");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RowLevelSecurity.Disable(migrationBuilder, "visit_product_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_movements_appointments_appointment_id",
                schema: "salon",
                table: "stock_movements");

            migrationBuilder.DropTable(
                name: "visit_product_lines",
                schema: "salon");

            migrationBuilder.DropIndex(
                name: "ix_stock_movements_appointment_id",
                schema: "salon",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "appointment_id",
                schema: "salon",
                table: "stock_movements");
        }
    }
}
