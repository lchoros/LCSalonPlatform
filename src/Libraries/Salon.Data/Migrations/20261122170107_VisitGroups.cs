using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class VisitGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid[]>(
                name: "extra_service_ids",
                schema: "salon",
                table: "phone_verifications",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddColumn<Guid>(
                name: "visit_group_id",
                schema: "salon",
                table: "appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_appointments_tenant_id_visit_group_id",
                schema: "salon",
                table: "appointments",
                columns: new[] { "tenant_id", "visit_group_id" },
                filter: "visit_group_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_appointments_tenant_id_visit_group_id",
                schema: "salon",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "extra_service_ids",
                schema: "salon",
                table: "phone_verifications");

            migrationBuilder.DropColumn(
                name: "visit_group_id",
                schema: "salon",
                table: "appointments");
        }
    }
}
