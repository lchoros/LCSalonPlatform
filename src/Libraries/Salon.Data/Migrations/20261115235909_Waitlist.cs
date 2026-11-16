using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class Waitlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "waitlist_entries",
                schema: "salon",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    not_before = table.Column<LocalDate>(type: "date", nullable: true),
                    not_after = table.Column<LocalDate>(type: "date", nullable: true),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    outcome = table.Column<int>(type: "integer", nullable: true),
                    closed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_waitlist_entries", x => x.id);
                    table.CheckConstraint("ck_waitlist_entries_closed_with_outcome", "(closed_at IS NULL) = (outcome IS NULL)");
                    table.CheckConstraint("ck_waitlist_entries_days_in_order", "not_before IS NULL OR not_after IS NULL OR not_after >= not_before");
                    table.ForeignKey(
                        name: "fk_waitlist_entries_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalSchema: "salon",
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_waitlist_entries_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "salon",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_waitlist_entries_services_service_id",
                        column: x => x.service_id,
                        principalSchema: "salon",
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_waitlist_entries_staff_members_staff_member_id",
                        column: x => x.staff_member_id,
                        principalSchema: "salon",
                        principalTable: "staff_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_waitlist_entries_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "salon",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_appointment_id",
                schema: "salon",
                table: "waitlist_entries",
                column: "appointment_id");

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_client_id",
                schema: "salon",
                table: "waitlist_entries",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_service_id",
                schema: "salon",
                table: "waitlist_entries",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_staff_member_id",
                schema: "salon",
                table: "waitlist_entries",
                column: "staff_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_tenant_id",
                schema: "salon",
                table: "waitlist_entries",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_waiting",
                schema: "salon",
                table: "waitlist_entries",
                columns: new[] { "tenant_id", "created_at" },
                filter: "closed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_waitlist_entries_one_wait_per_client_and_service",
                schema: "salon",
                table: "waitlist_entries",
                columns: new[] { "tenant_id", "client_id", "service_id" },
                unique: true,
                filter: "closed_at IS NULL");

            RowLevelSecurity.Enable(migrationBuilder, "waitlist_entries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RowLevelSecurity.Disable(migrationBuilder, "waitlist_entries");

            migrationBuilder.DropTable(
                name: "waitlist_entries",
                schema: "salon");
        }
    }
}
