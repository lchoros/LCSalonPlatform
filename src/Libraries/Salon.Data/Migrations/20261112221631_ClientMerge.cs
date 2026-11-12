using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClientMerge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Instant>(
                name: "merged_at",
                schema: "salon",
                table: "clients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "merged_into_client_id",
                schema: "salon",
                table: "clients",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_clients_merged_into_client_id",
                schema: "salon",
                table: "clients",
                column: "merged_into_client_id");

            migrationBuilder.AddForeignKey(
                name: "fk_clients_clients_merged_into_client_id",
                schema: "salon",
                table: "clients",
                column: "merged_into_client_id",
                principalSchema: "salon",
                principalTable: "clients",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_clients_clients_merged_into_client_id",
                schema: "salon",
                table: "clients");

            migrationBuilder.DropIndex(
                name: "ix_clients_merged_into_client_id",
                schema: "salon",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "merged_at",
                schema: "salon",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "merged_into_client_id",
                schema: "salon",
                table: "clients");
        }
    }
}
