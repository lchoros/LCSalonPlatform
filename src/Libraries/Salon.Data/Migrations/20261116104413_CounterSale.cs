using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class CounterSale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "client_id",
                schema: "salon",
                table: "client_account_entries",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateTable(
                name: "counter_sale_lines",
                schema: "salon",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_counter_sale_lines", x => x.id);
                    table.CheckConstraint("ck_counter_sale_lines_quantity_positive", "quantity > 0");
                    table.CheckConstraint("ck_counter_sale_lines_unit_price_not_negative", "unit_price >= 0");
                    table.ForeignKey(
                        name: "fk_counter_sale_lines_client_account_entries_payment_entry_id",
                        column: x => x.payment_entry_id,
                        principalSchema: "salon",
                        principalTable: "client_account_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_counter_sale_lines_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "salon",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_counter_sale_lines_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "salon",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_client_account_entries_client_unless_counter_sale",
                schema: "salon",
                table: "client_account_entries",
                sql: "client_id IS NOT NULL OR (kind = 8 AND appointment_id IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_counter_sale_lines_payment_entry_id",
                schema: "salon",
                table: "counter_sale_lines",
                column: "payment_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_counter_sale_lines_product_id",
                schema: "salon",
                table: "counter_sale_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_counter_sale_lines_tenant_id",
                schema: "salon",
                table: "counter_sale_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_counter_sale_lines_tenant_id_payment_entry_id_product_id",
                schema: "salon",
                table: "counter_sale_lines",
                columns: new[] { "tenant_id", "payment_entry_id", "product_id" },
                unique: true);

            RowLevelSecurity.Enable(migrationBuilder, "counter_sale_lines");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RowLevelSecurity.Disable(migrationBuilder, "counter_sale_lines");

            migrationBuilder.DropTable(
                name: "counter_sale_lines",
                schema: "salon");

            migrationBuilder.DropCheckConstraint(
                name: "ck_client_account_entries_client_unless_counter_sale",
                schema: "salon",
                table: "client_account_entries");

            migrationBuilder.AlterColumn<Guid>(
                name: "client_id",
                schema: "salon",
                table: "client_account_entries",
                type: "uuid",
                // No default on purpose: while a counter sale exists this step fails, loudly, instead of inventing a
                // client for money that has none (ADR 0053).
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
