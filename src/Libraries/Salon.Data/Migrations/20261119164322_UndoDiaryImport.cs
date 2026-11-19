using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class UndoDiaryImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "import_batch_id",
                schema: "salon",
                table: "appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_appointments_tenant_id_import_batch_id",
                schema: "salon",
                table: "appointments",
                columns: new[] { "tenant_id", "import_batch_id" },
                filter: "import_batch_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_appointments_tenant_id_import_batch_id",
                schema: "salon",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "import_batch_id",
                schema: "salon",
                table: "appointments");
        }
    }
}
