using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class PublicBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "public_token",
                schema: "salon",
                table: "appointments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "phone_verifications",
                schema: "salon",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone_e164 = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    code_salt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    verified_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    start_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    marketing_consent = table.Column<bool>(type: "boolean", nullable: false),
                    consent_wording = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_phone_verifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_phone_verifications_services_service_id",
                        column: x => x.service_id,
                        principalSchema: "salon",
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_phone_verifications_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "salon",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_public_token",
                schema: "salon",
                table: "appointments",
                column: "public_token",
                unique: true,
                filter: "public_token IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_phone_verifications_service_id",
                schema: "salon",
                table: "phone_verifications",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_phone_verifications_tenant_id",
                schema: "salon",
                table: "phone_verifications",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_phone_verifications_tenant_id_phone_e164_created_at",
                schema: "salon",
                table: "phone_verifications",
                columns: new[] { "tenant_id", "phone_e164", "created_at" });

            RowLevelSecurity.Enable(migrationBuilder, "phone_verifications");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RowLevelSecurity.Disable(migrationBuilder, "phone_verifications");

            migrationBuilder.DropTable(
                name: "phone_verifications",
                schema: "salon");

            migrationBuilder.DropIndex(
                name: "ix_appointments_public_token",
                schema: "salon",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "public_token",
                schema: "salon",
                table: "appointments");
        }
    }
}
