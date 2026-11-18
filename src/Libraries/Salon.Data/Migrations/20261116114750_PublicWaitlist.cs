using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class PublicWaitlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "purpose",
                schema: "salon",
                table: "phone_verifications",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<LocalDate>(
                name: "wait_not_after",
                schema: "salon",
                table: "phone_verifications",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<LocalDate>(
                name: "wait_not_before",
                schema: "salon",
                table: "phone_verifications",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "wait_note",
                schema: "salon",
                table: "phone_verifications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "purpose",
                schema: "salon",
                table: "phone_verifications");

            migrationBuilder.DropColumn(
                name: "wait_not_after",
                schema: "salon",
                table: "phone_verifications");

            migrationBuilder.DropColumn(
                name: "wait_not_before",
                schema: "salon",
                table: "phone_verifications");

            migrationBuilder.DropColumn(
                name: "wait_note",
                schema: "salon",
                table: "phone_verifications");
        }
    }
}
