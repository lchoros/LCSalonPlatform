using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class MessageTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "message_templates",
                schema: "salon",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    culture = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    body = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_message_templates_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "salon",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_message_templates_tenant_id",
                schema: "salon",
                table: "message_templates",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_templates_tenant_id_key_channel_culture",
                schema: "salon",
                table: "message_templates",
                columns: new[] { "tenant_id", "key", "channel", "culture" },
                unique: true);

            RowLevelSecurity.Enable(migrationBuilder, "message_templates");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RowLevelSecurity.Disable(migrationBuilder, "message_templates");

            migrationBuilder.DropTable(
                name: "message_templates",
                schema: "salon");
        }
    }
}
