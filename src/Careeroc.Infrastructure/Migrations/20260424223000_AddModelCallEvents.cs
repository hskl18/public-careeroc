using System;
using Careeroc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Careeroc.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(CareerocDbContext))]
    [Migration("20260424223000_AddModelCallEvents")]
    public partial class AddModelCallEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "model_call_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    model = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    purpose = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    outcome = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    user_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    inbox_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inbox_email_id = table.Column<Guid>(type: "uuid", nullable: true),
                    run_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    prompt_tokens = table.Column<int>(type: "integer", nullable: true),
                    completion_tokens = table.Column<int>(type: "integer", nullable: true),
                    total_tokens = table.Column<int>(type: "integer", nullable: true),
                    estimated_cost_usd = table.Column<decimal>(type: "numeric(12,6)", nullable: true),
                    latency_ms = table.Column<int>(type: "integer", nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: true),
                    error_type = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_model_call_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_model_call_events_inbox_account_id",
                table: "model_call_events",
                column: "inbox_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_model_call_events_occurred_at_utc",
                table: "model_call_events",
                column: "occurred_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_model_call_events_purpose_occurred_at_utc",
                table: "model_call_events",
                columns: new[] { "purpose", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_model_call_events_user_email",
                table: "model_call_events",
                column: "user_email");

            migrationBuilder.CreateIndex(
                name: "ix_model_call_events_user_email_occurred_at_utc",
                table: "model_call_events",
                columns: new[] { "user_email", "occurred_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "model_call_events");
        }
    }
}
