using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Salon.Data.Migrations
{
    /// <inheritdoc />
    public partial class DailySummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_summary_deliveries",
                schema: "salon",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<LocalDate>(type: "date", nullable: false),
                    sent_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    recipients = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_summary_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "fk_daily_summary_deliveries_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "salon",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_daily_summary_deliveries_tenant_id",
                schema: "salon",
                table: "daily_summary_deliveries",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_daily_summary_deliveries_tenant_id_day",
                schema: "salon",
                table: "daily_summary_deliveries",
                columns: new[] { "tenant_id", "day" },
                unique: true);

            RowLevelSecurity.Enable(migrationBuilder, "daily_summary_deliveries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RowLevelSecurity.Disable(migrationBuilder, "daily_summary_deliveries");

            migrationBuilder.DropTable(
                name: "daily_summary_deliveries",
                schema: "salon");
        }
    }
}
