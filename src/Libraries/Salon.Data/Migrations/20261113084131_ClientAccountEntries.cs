using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClientAccountEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_account_entries",
                schema: "salon",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    method = table.Column<int>(type: "integer", nullable: true),
                    provider_system_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    provider_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    related_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_account_entries", x => x.id);
                    table.CheckConstraint("ck_client_account_entries_amount_positive", "amount > 0");
                    table.ForeignKey(
                        name: "fk_client_account_entries_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalSchema: "salon",
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_client_account_entries_client_account_entries_related_entry",
                        column: x => x.related_entry_id,
                        principalSchema: "salon",
                        principalTable: "client_account_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_client_account_entries_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "salon",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_client_account_entries_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "salon",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_client_account_entries_appointment_id",
                schema: "salon",
                table: "client_account_entries",
                column: "appointment_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_account_entries_client_id",
                schema: "salon",
                table: "client_account_entries",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_account_entries_related_entry_id",
                schema: "salon",
                table: "client_account_entries",
                column: "related_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_account_entries_tenant_id",
                schema: "salon",
                table: "client_account_entries",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_account_entries_tenant_id_client_id_at",
                schema: "salon",
                table: "client_account_entries",
                columns: new[] { "tenant_id", "client_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ux_client_account_entries_one_fee_per_appointment",
                schema: "salon",
                table: "client_account_entries",
                columns: new[] { "tenant_id", "appointment_id" },
                unique: true,
                filter: "kind IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "ux_client_account_entries_one_settlement_per_fee",
                schema: "salon",
                table: "client_account_entries",
                columns: new[] { "tenant_id", "related_entry_id" },
                unique: true,
                filter: "kind IN (3, 4)");

            RowLevelSecurity.Enable(migrationBuilder, "client_account_entries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RowLevelSecurity.Disable(migrationBuilder, "client_account_entries");

            migrationBuilder.DropTable(
                name: "client_account_entries",
                schema: "salon");
        }
    }
}
