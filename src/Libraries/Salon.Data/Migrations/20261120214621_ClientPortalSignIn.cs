using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClientPortalSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "service_id",
                schema: "salon",
                table: "phone_verifications",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddCheckConstraint(
                name: "ck_phone_verifications_service_unless_client_portal",
                schema: "salon",
                table: "phone_verifications",
                sql: "service_id IS NOT NULL OR purpose = 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sign-in rows (purpose 3, ADR 0083) carry no service and cannot survive the column becoming required
            // again. They are short-lived by design (ten minutes of use, a day of retention), so they are removed.
            // Under forced row-level security a role that is not a superuser sees none of them here; the ALTER below
            // then fails instead of inventing a service: switch Features:ClientPortal off, wait a day for the
            // retention task to clear them, and run this again.
            migrationBuilder.Sql("DELETE FROM salon.phone_verifications WHERE service_id IS NULL;");

            migrationBuilder.DropCheckConstraint(
                name: "ck_phone_verifications_service_unless_client_portal",
                schema: "salon",
                table: "phone_verifications");

            migrationBuilder.AlterColumn<Guid>(
                name: "service_id",
                schema: "salon",
                table: "phone_verifications",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
