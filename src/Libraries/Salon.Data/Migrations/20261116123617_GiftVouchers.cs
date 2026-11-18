using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class GiftVouchers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_client_account_entries_client_unless_counter_sale",
                schema: "salon",
                table: "client_account_entries");

            migrationBuilder.CreateTable(
                name: "gift_vouchers",
                schema: "salon",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    initial_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    balance = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    sold_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    sold_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sale_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_on = table.Column<LocalDate>(type: "date", nullable: true),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    refunded_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gift_vouchers", x => x.id);
                    table.CheckConstraint("ck_gift_vouchers_balance_within_initial", "balance >= 0 AND balance <= initial_amount");
                    table.CheckConstraint("ck_gift_vouchers_initial_amount_positive", "initial_amount > 0");
                    table.ForeignKey(
                        name: "fk_gift_vouchers_client_account_entries_sale_entry_id",
                        column: x => x.sale_entry_id,
                        principalSchema: "salon",
                        principalTable: "client_account_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_gift_vouchers_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "salon",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_client_account_entries_one_use_of_a_voucher_per_appointment",
                schema: "salon",
                table: "client_account_entries",
                columns: new[] { "tenant_id", "appointment_id", "related_entry_id" },
                unique: true,
                filter: "kind = 11");

            migrationBuilder.AddCheckConstraint(
                name: "ck_client_account_entries_client_unless_counter_sale",
                schema: "salon",
                table: "client_account_entries",
                sql: "client_id IS NOT NULL OR (kind IN (8, 9, 10, 12) AND appointment_id IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_gift_vouchers_sale_entry_id",
                schema: "salon",
                table: "gift_vouchers",
                column: "sale_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_gift_vouchers_tenant_id",
                schema: "salon",
                table: "gift_vouchers",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_gift_vouchers_tenant_id_code",
                schema: "salon",
                table: "gift_vouchers",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gift_vouchers_tenant_id_sale_entry_id",
                schema: "salon",
                table: "gift_vouchers",
                columns: new[] { "tenant_id", "sale_entry_id" },
                unique: true);

            RowLevelSecurity.Enable(migrationBuilder, "gift_vouchers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RowLevelSecurity.Disable(migrationBuilder, "gift_vouchers");

            migrationBuilder.DropTable(
                name: "gift_vouchers",
                schema: "salon");

            migrationBuilder.DropIndex(
                name: "ux_client_account_entries_one_use_of_a_voucher_per_appointment",
                schema: "salon",
                table: "client_account_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_client_account_entries_client_unless_counter_sale",
                schema: "salon",
                table: "client_account_entries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_client_account_entries_client_unless_counter_sale",
                schema: "salon",
                table: "client_account_entries",
                sql: "client_id IS NOT NULL OR (kind IN (8, 9) AND appointment_id IS NULL)");
        }
    }
}
