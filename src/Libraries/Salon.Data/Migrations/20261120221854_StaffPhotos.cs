using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class StaffPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "staff_photos",
                schema: "salon",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_photos", x => x.id);
                    table.ForeignKey(
                        name: "fk_staff_photos_staff_members_staff_member_id",
                        column: x => x.staff_member_id,
                        principalSchema: "salon",
                        principalTable: "staff_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_staff_photos_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "salon",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_staff_photos_tenant_id",
                schema: "salon",
                table: "staff_photos",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_staff_photos_staff_member_id",
                schema: "salon",
                table: "staff_photos",
                column: "staff_member_id",
                unique: true);

            // Tenant-owned (ADR 0002): the database itself keeps one salon's faces from another.
            RowLevelSecurity.Enable(migrationBuilder, "staff_photos");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RowLevelSecurity.Disable(migrationBuilder, "staff_photos");

            migrationBuilder.DropTable(
                name: "staff_photos",
                schema: "salon");
        }
    }
}
