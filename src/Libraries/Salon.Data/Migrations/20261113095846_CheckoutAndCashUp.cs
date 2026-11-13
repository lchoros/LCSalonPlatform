using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class CheckoutAndCashUp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cash_ups",
                schema: "salon",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<LocalDate>(type: "date", nullable: false),
                    expected_cash = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    counted_cash = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    closed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    closed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cash_ups", x => x.id);
                    table.CheckConstraint("ck_cash_ups_counted_cash_not_negative", "counted_cash >= 0");
                    table.ForeignKey(
                        name: "fk_cash_ups_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "salon",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_client_account_entries_tenant_id_at",
                schema: "salon",
                table: "client_account_entries",
                columns: new[] { "tenant_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ux_client_account_entries_one_visit_payment_per_appointment",
                schema: "salon",
                table: "client_account_entries",
                columns: new[] { "tenant_id", "appointment_id" },
                unique: true,
                filter: "kind = 7");

            migrationBuilder.CreateIndex(
                name: "ix_cash_ups_tenant_id",
                schema: "salon",
                table: "cash_ups",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_cash_ups_tenant_id_day",
                schema: "salon",
                table: "cash_ups",
                columns: new[] { "tenant_id", "day" },
                unique: true);

            RowLevelSecurity.Enable(migrationBuilder, "cash_ups");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RowLevelSecurity.Disable(migrationBuilder, "cash_ups");

            migrationBuilder.DropTable(
                name: "cash_ups",
                schema: "salon");

            migrationBuilder.DropIndex(
                name: "ix_client_account_entries_tenant_id_at",
                schema: "salon",
                table: "client_account_entries");

            migrationBuilder.DropIndex(
                name: "ux_client_account_entries_one_visit_payment_per_appointment",
                schema: "salon",
                table: "client_account_entries");
        }
    }
}
