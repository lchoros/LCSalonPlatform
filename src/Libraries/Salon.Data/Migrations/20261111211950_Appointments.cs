using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class Appointments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "appointments",
                schema: "salon",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    price_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelled_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointments", x => x.id);
                    table.ForeignKey(
                        name: "fk_appointments_services_service_id",
                        column: x => x.service_id,
                        principalSchema: "salon",
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_staff_members_staff_member_id",
                        column: x => x.staff_member_id,
                        principalSchema: "salon",
                        principalTable: "staff_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "salon",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "appointment_status_changes",
                schema: "salon",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from = table.Column<int>(type: "integer", nullable: true),
                    to = table.Column<int>(type: "integer", nullable: false),
                    at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointment_status_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_appointment_status_changes_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalSchema: "salon",
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_appointment_status_changes_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "salon",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_appointment_status_changes_appointment_id_at",
                schema: "salon",
                table: "appointment_status_changes",
                columns: new[] { "appointment_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_appointment_status_changes_tenant_id",
                schema: "salon",
                table: "appointment_status_changes",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_service_id",
                schema: "salon",
                table: "appointments",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_staff_member_id",
                schema: "salon",
                table: "appointments",
                column: "staff_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_tenant_id",
                schema: "salon",
                table: "appointments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_tenant_id_staff_member_id_start_at",
                schema: "salon",
                table: "appointments",
                columns: new[] { "tenant_id", "staff_member_id", "start_at" });

            // ADR 0012: the database, not the application, refuses two active appointments (status 1-3) of one person at the
            // same time, whatever code path inserts them. btree_gist is a trusted contrib extension (CREATE on the database suffices).
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
            migrationBuilder.Sql("""
                ALTER TABLE salon.appointments
                    ADD CONSTRAINT ex_appointments_staff_time
                    EXCLUDE USING gist (staff_member_id WITH =, tstzrange(start_at, end_at, '[)') WITH &&)
                    WHERE (status IN (1, 2, 3));
                """);

            RowLevelSecurity.Enable(migrationBuilder, "appointments");
            RowLevelSecurity.Enable(migrationBuilder, "appointment_status_changes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RowLevelSecurity.Disable(migrationBuilder, "appointment_status_changes");
            RowLevelSecurity.Disable(migrationBuilder, "appointments");

            // The btree_gist extension stays: it is shared and harmless, and dropping it could break another table later.
            migrationBuilder.Sql("ALTER TABLE salon.appointments DROP CONSTRAINT IF EXISTS ex_appointments_staff_time;");

            migrationBuilder.DropTable(
                name: "appointment_status_changes",
                schema: "salon");

            migrationBuilder.DropTable(
                name: "appointments",
                schema: "salon");
        }
    }
}
