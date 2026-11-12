using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class Staff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "staff_members",
                schema: "salon",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    colour = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_bookable = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_staff_members_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "salon",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "working_hours",
                schema: "salon",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<int>(type: "integer", nullable: false),
                    start = table.Column<LocalTime>(type: "time", nullable: false),
                    end = table.Column<LocalTime>(type: "time", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_working_hours", x => x.id);
                    table.ForeignKey(
                        name: "fk_working_hours_staff_members_staff_member_id",
                        column: x => x.staff_member_id,
                        principalSchema: "salon",
                        principalTable: "staff_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_working_hours_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "salon",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_staff_members_tenant_id",
                schema: "salon",
                table: "staff_members",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_staff_members_tenant_id_sort_order",
                schema: "salon",
                table: "staff_members",
                columns: new[] { "tenant_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_working_hours_staff_member_id",
                schema: "salon",
                table: "working_hours",
                column: "staff_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_working_hours_tenant_id",
                schema: "salon",
                table: "working_hours",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_working_hours_tenant_id_staff_member_id_day_start",
                schema: "salon",
                table: "working_hours",
                columns: new[] { "tenant_id", "staff_member_id", "day", "start" },
                unique: true);

            RowLevelSecurity.Enable(migrationBuilder, "staff_members");
            RowLevelSecurity.Enable(migrationBuilder, "working_hours");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RowLevelSecurity.Disable(migrationBuilder, "working_hours");
            RowLevelSecurity.Disable(migrationBuilder, "staff_members");

            migrationBuilder.DropTable(
                name: "working_hours",
                schema: "salon");

            migrationBuilder.DropTable(
                name: "staff_members",
                schema: "salon");
        }
    }
}
