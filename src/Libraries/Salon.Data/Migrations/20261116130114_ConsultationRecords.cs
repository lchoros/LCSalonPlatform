using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConsultationRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consultation_records",
                schema: "salon",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    taken_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    taken_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    taken_for = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    answers_protected = table.Column<string>(type: "text", nullable: false),
                    consent_wording = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    form_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consultation_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_consultation_records_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "salon",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_consultation_records_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "salon",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_consultation_records_client_id",
                schema: "salon",
                table: "consultation_records",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_consultation_records_tenant_id",
                schema: "salon",
                table: "consultation_records",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_consultation_records_tenant_id_client_id_taken_at",
                schema: "salon",
                table: "consultation_records",
                columns: new[] { "tenant_id", "client_id", "taken_at" });

            RowLevelSecurity.Enable(migrationBuilder, "consultation_records");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RowLevelSecurity.Disable(migrationBuilder, "consultation_records");

            migrationBuilder.DropTable(
                name: "consultation_records",
                schema: "salon");
        }
    }
}
