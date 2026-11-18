using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class WaitlistOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "waitlist_offers",
                schema: "salon",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    waitlist_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    end = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    token = table.Column<string>(type: "character varying(22)", maxLength: 22, nullable: false),
                    offered_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    held_until = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    outcome = table.Column<int>(type: "integer", nullable: true),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    freed_by_appointment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ended_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_waitlist_offers", x => x.id);
                    table.CheckConstraint("ck_waitlist_offers_ended_with_outcome", "(ended_at IS NULL) = (outcome IS NULL)");
                    table.CheckConstraint("ck_waitlist_offers_ends_after_start", "\"end\" > start");
                    table.ForeignKey(
                        name: "fk_waitlist_offers_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalSchema: "salon",
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_waitlist_offers_appointments_freed_by_appointment_id",
                        column: x => x.freed_by_appointment_id,
                        principalSchema: "salon",
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_waitlist_offers_services_service_id",
                        column: x => x.service_id,
                        principalSchema: "salon",
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_waitlist_offers_staff_members_staff_member_id",
                        column: x => x.staff_member_id,
                        principalSchema: "salon",
                        principalTable: "staff_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_waitlist_offers_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "salon",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_waitlist_offers_waitlist_entries_waitlist_entry_id",
                        column: x => x.waitlist_entry_id,
                        principalSchema: "salon",
                        principalTable: "waitlist_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_offers_appointment_id",
                schema: "salon",
                table: "waitlist_offers",
                column: "appointment_id");

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_offers_freed_by_appointment_id",
                schema: "salon",
                table: "waitlist_offers",
                column: "freed_by_appointment_id");

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_offers_service_id",
                schema: "salon",
                table: "waitlist_offers",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_offers_staff_member_id",
                schema: "salon",
                table: "waitlist_offers",
                column: "staff_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_offers_tenant_id",
                schema: "salon",
                table: "waitlist_offers",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_offers_waitlist_entry_id",
                schema: "salon",
                table: "waitlist_offers",
                column: "waitlist_entry_id");

            migrationBuilder.CreateIndex(
                name: "ux_waitlist_offers_one_standing_per_entry",
                schema: "salon",
                table: "waitlist_offers",
                columns: new[] { "tenant_id", "waitlist_entry_id" },
                unique: true,
                filter: "ended_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_waitlist_offers_one_standing_per_time",
                schema: "salon",
                table: "waitlist_offers",
                columns: new[] { "tenant_id", "staff_member_id", "start" },
                unique: true,
                filter: "ended_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_waitlist_offers_token",
                schema: "salon",
                table: "waitlist_offers",
                column: "token",
                unique: true);

            // Tenant-owned (ADR 0003): the database itself keeps one salon's offers from another.
            RowLevelSecurity.Enable(migrationBuilder, "waitlist_offers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RowLevelSecurity.Disable(migrationBuilder, "waitlist_offers");

            migrationBuilder.DropTable(
                name: "waitlist_offers",
                schema: "salon");
        }
    }
}
