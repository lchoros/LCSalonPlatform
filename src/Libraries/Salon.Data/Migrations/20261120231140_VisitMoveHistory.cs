using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class VisitMoveHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "moved_from_staff_member_id",
                schema: "salon",
                table: "appointment_status_changes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "moved_from_start",
                schema: "salon",
                table: "appointment_status_changes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "moved_to_staff_member_id",
                schema: "salon",
                table: "appointment_status_changes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "moved_to_start",
                schema: "salon",
                table: "appointment_status_changes",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "moved_from_staff_member_id",
                schema: "salon",
                table: "appointment_status_changes");

            migrationBuilder.DropColumn(
                name: "moved_from_start",
                schema: "salon",
                table: "appointment_status_changes");

            migrationBuilder.DropColumn(
                name: "moved_to_staff_member_id",
                schema: "salon",
                table: "appointment_status_changes");

            migrationBuilder.DropColumn(
                name: "moved_to_start",
                schema: "salon",
                table: "appointment_status_changes");
        }
    }
}
