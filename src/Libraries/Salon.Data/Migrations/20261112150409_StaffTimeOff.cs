using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class StaffTimeOff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "staff_time_off",
                schema: "salon",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_date = table.Column<LocalDate>(type: "date", nullable: false),
                    to_date = table.Column<LocalDate>(type: "date", nullable: false),
                    start = table.Column<LocalTime>(type: "time", nullable: true),
                    end = table.Column<LocalTime>(type: "time", nullable: true),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_time_off", x => x.id);
                    table.ForeignKey(
                        name: "fk_staff_time_off_staff_members_staff_member_id",
                        column: x => x.staff_member_id,
                        principalSchema: "salon",
                        principalTable: "staff_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_staff_time_off_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "salon",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_staff_time_off_staff_member_id",
                schema: "salon",
                table: "staff_time_off",
                column: "staff_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_staff_time_off_tenant_id",
                schema: "salon",
                table: "staff_time_off",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_staff_time_off_tenant_id_staff_member_id_from_date",
                schema: "salon",
                table: "staff_time_off",
                columns: new[] { "tenant_id", "staff_member_id", "from_date" });

            RowLevelSecurity.Enable(migrationBuilder, "staff_time_off");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RowLevelSecurity.Disable(migrationBuilder, "staff_time_off");

            migrationBuilder.DropTable(
                name: "staff_time_off",
                schema: "salon");
        }
    }
}
