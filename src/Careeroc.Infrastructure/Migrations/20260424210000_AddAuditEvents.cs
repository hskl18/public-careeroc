using System;
using Careeroc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Careeroc.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(CareerocDbContext))]
    [Migration("20260424210000_AddAuditEvents")]
    public partial class AddAuditEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    event_type = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    outcome = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    user_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    target_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    resource_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    method = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    remote_ip = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_event_type_occurred_at_utc",
                table: "audit_events",
                columns: new[] { "event_type", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_occurred_at_utc",
                table: "audit_events",
                column: "occurred_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_user_email",
                table: "audit_events",
                column: "user_email");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events");
        }
    }
}
