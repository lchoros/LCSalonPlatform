using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class StaffServiceOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "staff_service_overrides",
                schema: "salon",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    duration = table.Column<Duration>(type: "interval", nullable: true),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    offers = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_service_overrides", x => x.id);
                    table.ForeignKey(
                        name: "fk_staff_service_overrides_services_service_id",
                        column: x => x.service_id,
                        principalSchema: "salon",
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_staff_service_overrides_staff_members_staff_member_id",
                        column: x => x.staff_member_id,
                        principalSchema: "salon",
                        principalTable: "staff_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_staff_service_overrides_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "salon",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_staff_service_overrides_service_id",
                schema: "salon",
                table: "staff_service_overrides",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_staff_service_overrides_staff_member_id",
                schema: "salon",
                table: "staff_service_overrides",
                column: "staff_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_staff_service_overrides_tenant_id",
                schema: "salon",
                table: "staff_service_overrides",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_staff_service_overrides_tenant_id_service_id",
                schema: "salon",
                table: "staff_service_overrides",
                columns: new[] { "tenant_id", "service_id" });

            migrationBuilder.CreateIndex(
                name: "ix_staff_service_overrides_tenant_id_staff_member_id_service_id",
                schema: "salon",
                table: "staff_service_overrides",
                columns: new[] { "tenant_id", "staff_member_id", "service_id" },
                unique: true);

            RowLevelSecurity.Enable(migrationBuilder, "staff_service_overrides");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RowLevelSecurity.Disable(migrationBuilder, "staff_service_overrides");

            migrationBuilder.DropTable(
                name: "staff_service_overrides",
                schema: "salon");
        }
    }
}
