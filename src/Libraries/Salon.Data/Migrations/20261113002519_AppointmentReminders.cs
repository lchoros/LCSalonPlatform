using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class AppointmentReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "appointment_reminders",
                schema: "salon",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    appointment_start = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    outcome = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointment_reminders", x => x.id);
                    table.ForeignKey(
                        name: "fk_appointment_reminders_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalSchema: "salon",
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_appointment_reminders_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "salon",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_appointment_reminders_appointment_id",
                schema: "salon",
                table: "appointment_reminders",
                column: "appointment_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointment_reminders_tenant_id",
                schema: "salon",
                table: "appointment_reminders",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointment_reminders_tenant_id_appointment_id_kind_appoint",
                schema: "salon",
                table: "appointment_reminders",
                columns: new[] { "tenant_id", "appointment_id", "kind", "appointment_start" },
                unique: true);

            RowLevelSecurity.Enable(migrationBuilder, "appointment_reminders");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RowLevelSecurity.Disable(migrationBuilder, "appointment_reminders");

            migrationBuilder.DropTable(
                name: "appointment_reminders",
                schema: "salon");
        }
    }
}
