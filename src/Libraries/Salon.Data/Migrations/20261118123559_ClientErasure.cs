using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClientErasure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Instant>(
                name: "erased_at",
                schema: "salon",
                table: "clients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "erased_by_user_id",
                schema: "salon",
                table: "clients",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "erased_at",
                schema: "salon",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "erased_by_user_id",
                schema: "salon",
                table: "clients");
        }
    }
}
