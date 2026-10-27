using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Careeroc.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveModelUsageRollups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "model_usage_daily_rollups");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "model_usage_daily_rollups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    blocked_count = table.Column<int>(type: "integer", nullable: false),
                    call_count = table.Column<int>(type: "integer", nullable: false),
                    completion_tokens = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    estimated_cost_usd = table.Column<decimal>(type: "numeric(12,6)", nullable: false),
                    failure_count = table.Column<int>(type: "integer", nullable: false),
                    inbox_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    prompt_tokens = table.Column<int>(type: "integer", nullable: false),
                    provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    purpose = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    success_count = table.Column<int>(type: "integer", nullable: false),
                    total_latency_ms = table.Column<int>(type: "integer", nullable: false),
                    total_tokens = table.Column<int>(type: "integer", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    usage_date = table.Column<DateOnly>(type: "date", nullable: false),
                    user_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_model_usage_daily_rollups", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_model_usage_daily_rollups_inbox_account_id_usage_date",
                table: "model_usage_daily_rollups",
                columns: new[] { "inbox_account_id", "usage_date" });

            migrationBuilder.CreateIndex(
                name: "ix_model_usage_daily_rollups_usage_date",
                table: "model_usage_daily_rollups",
                column: "usage_date");

            migrationBuilder.CreateIndex(
                name: "ix_model_usage_daily_rollups_user_email_usage_date",
                table: "model_usage_daily_rollups",
                columns: new[] { "user_email", "usage_date" });

            migrationBuilder.CreateIndex(
                name: "ix_model_usage_rollups_day_user_account_model_purpose",
                table: "model_usage_daily_rollups",
                columns: new[] { "usage_date", "user_email", "inbox_account_id", "provider", "model", "purpose" },
                unique: true);
        }
    }
}
