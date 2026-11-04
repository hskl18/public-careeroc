using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Careeroc.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailProcessingDeadLetterFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "dead_letter_reason",
                table: "email_processing_jobs",
                type: "character varying(280)",
                maxLength: 280,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "dead_lettered_at_utc",
                table: "email_processing_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_email_processing_jobs_status_dead_lettered_at_utc",
                table: "email_processing_jobs",
                columns: new[] { "status", "dead_lettered_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_email_processing_jobs_status_dead_lettered_at_utc",
                table: "email_processing_jobs");

            migrationBuilder.DropColumn(
                name: "dead_letter_reason",
                table: "email_processing_jobs");

            migrationBuilder.DropColumn(
                name: "dead_lettered_at_utc",
                table: "email_processing_jobs");
        }
    }
}
