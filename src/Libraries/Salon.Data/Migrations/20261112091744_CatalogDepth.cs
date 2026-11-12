using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class CatalogDepth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "buffer_after_minutes",
                schema: "salon",
                table: "services",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "category_id",
                schema: "salon",
                table: "services",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "processing_minutes",
                schema: "salon",
                table: "services",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "processing_starts_after_minutes",
                schema: "salon",
                table: "services",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "service_categories",
                schema: "salon",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_categories", x => x.id);
                    table.ForeignKey(
                        name: "fk_service_categories_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "salon",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_services_category_id",
                schema: "salon",
                table: "services",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_categories_tenant_id",
                schema: "salon",
                table: "service_categories",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_categories_tenant_id_name",
                schema: "salon",
                table: "service_categories",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_service_categories_tenant_id_sort_order",
                schema: "salon",
                table: "service_categories",
                columns: new[] { "tenant_id", "sort_order" });

            migrationBuilder.AddForeignKey(
                name: "fk_services_service_categories_category_id",
                schema: "salon",
                table: "services",
                column: "category_id",
                principalSchema: "salon",
                principalTable: "service_categories",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            RowLevelSecurity.Enable(migrationBuilder, "service_categories");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RowLevelSecurity.Disable(migrationBuilder, "service_categories");

            migrationBuilder.DropForeignKey(
                name: "fk_services_service_categories_category_id",
                schema: "salon",
                table: "services");

            migrationBuilder.DropTable(
                name: "service_categories",
                schema: "salon");

            migrationBuilder.DropIndex(
                name: "ix_services_category_id",
                schema: "salon",
                table: "services");

            migrationBuilder.DropColumn(
                name: "buffer_after_minutes",
                schema: "salon",
                table: "services");

            migrationBuilder.DropColumn(
                name: "category_id",
                schema: "salon",
                table: "services");

            migrationBuilder.DropColumn(
                name: "processing_minutes",
                schema: "salon",
                table: "services");

            migrationBuilder.DropColumn(
                name: "processing_starts_after_minutes",
                schema: "salon",
                table: "services");
        }
    }
}
