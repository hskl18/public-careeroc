using System;
using Careeroc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Careeroc.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(CareerocDbContext))]
    [Migration("20260424190000_AddReminderEvidenceRefreshCursor")]
    public partial class AddReminderEvidenceRefreshCursor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "evidence_refreshed_at_utc",
                table: "reminders",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "evidence_refreshed_at_utc",
                table: "reminders");
        }
    }
}
