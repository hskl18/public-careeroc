using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Careeroc.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTesterRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tester_requests");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tester_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    remote_ip = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    request_count = table.Column<int>(type: "integer", nullable: false),
                    source_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, defaultValue: "new"),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tester_requests", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tester_requests_email",
                table: "tester_requests",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tester_requests_status",
                table: "tester_requests",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_tester_requests_updated_at_utc",
                table: "tester_requests",
                column: "updated_at_utc");
        }
    }
}
