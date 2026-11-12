using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class AppointmentClientFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_appointments_client_id",
                schema: "salon",
                table: "appointments",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_tenant_id_client_id",
                schema: "salon",
                table: "appointments",
                columns: new[] { "tenant_id", "client_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_appointments_clients_client_id",
                schema: "salon",
                table: "appointments",
                column: "client_id",
                principalSchema: "salon",
                principalTable: "clients",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_appointments_clients_client_id",
                schema: "salon",
                table: "appointments");

            migrationBuilder.DropIndex(
                name: "ix_appointments_client_id",
                schema: "salon",
                table: "appointments");

            migrationBuilder.DropIndex(
                name: "ix_appointments_tenant_id_client_id",
                schema: "salon",
                table: "appointments");
        }
    }
}
