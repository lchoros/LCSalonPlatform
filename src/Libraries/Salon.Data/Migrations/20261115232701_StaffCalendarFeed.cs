using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class StaffCalendarFeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "calendar_feed_token",
                schema: "salon",
                table: "staff_members",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_staff_members_calendar_feed_token",
                schema: "salon",
                table: "staff_members",
                column: "calendar_feed_token",
                unique: true,
                filter: "calendar_feed_token IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_staff_members_calendar_feed_token",
                schema: "salon",
                table: "staff_members");

            migrationBuilder.DropColumn(
                name: "calendar_feed_token",
                schema: "salon",
                table: "staff_members");
        }
    }
}
