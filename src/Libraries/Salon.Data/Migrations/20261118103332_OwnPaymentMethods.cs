using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class OwnPaymentMethods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "custom_method_id",
                schema: "salon",
                table: "client_account_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "method_name",
                schema: "salon",
                table: "client_account_entries",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "salon_payment_methods",
                schema: "salon",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_salon_payment_methods", x => x.id);
                    table.ForeignKey(
                        name: "fk_salon_payment_methods_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "salon",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_client_account_entries_custom_method_id",
                schema: "salon",
                table: "client_account_entries",
                column: "custom_method_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_client_account_entries_own_method_named",
                schema: "salon",
                table: "client_account_entries",
                sql: "(method IS NOT DISTINCT FROM 4 AND custom_method_id IS NOT NULL AND method_name IS NOT NULL) OR (method IS DISTINCT FROM 4 AND custom_method_id IS NULL AND method_name IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_salon_payment_methods_tenant_id",
                schema: "salon",
                table: "salon_payment_methods",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_salon_payment_methods_tenant_id_sort_order",
                schema: "salon",
                table: "salon_payment_methods",
                columns: new[] { "tenant_id", "sort_order" });

            migrationBuilder.AddForeignKey(
                name: "fk_client_account_entries_salon_payment_methods_custom_method_",
                schema: "salon",
                table: "client_account_entries",
                column: "custom_method_id",
                principalSchema: "salon",
                principalTable: "salon_payment_methods",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            RowLevelSecurity.Enable(migrationBuilder, "salon_payment_methods");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RowLevelSecurity.Disable(migrationBuilder, "salon_payment_methods");

            migrationBuilder.DropForeignKey(
                name: "fk_client_account_entries_salon_payment_methods_custom_method_",
                schema: "salon",
                table: "client_account_entries");

            migrationBuilder.DropTable(
                name: "salon_payment_methods",
                schema: "salon");

            migrationBuilder.DropIndex(
                name: "ix_client_account_entries_custom_method_id",
                schema: "salon",
                table: "client_account_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_client_account_entries_own_method_named",
                schema: "salon",
                table: "client_account_entries");

            migrationBuilder.DropColumn(
                name: "custom_method_id",
                schema: "salon",
                table: "client_account_entries");

            migrationBuilder.DropColumn(
                name: "method_name",
                schema: "salon",
                table: "client_account_entries");
        }
    }
}
