using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Careeroc.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddManualReviewMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "classification_confidence",
                table: "inbox_emails",
                type: "numeric(4,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "matching_confidence",
                table: "inbox_emails",
                type: "numeric(4,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "requires_manual_review",
                table: "inbox_emails",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "review_reason",
                table: "inbox_emails",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reviewed_at_utc",
                table: "inbox_emails",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_inbox_emails_requires_manual_review_received_at_utc",
                table: "inbox_emails",
                columns: new[] { "requires_manual_review", "received_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_inbox_emails_requires_manual_review_received_at_utc",
                table: "inbox_emails");

            migrationBuilder.DropColumn(
                name: "classification_confidence",
                table: "inbox_emails");

            migrationBuilder.DropColumn(
                name: "matching_confidence",
                table: "inbox_emails");

            migrationBuilder.DropColumn(
                name: "requires_manual_review",
                table: "inbox_emails");

            migrationBuilder.DropColumn(
                name: "review_reason",
                table: "inbox_emails");

            migrationBuilder.DropColumn(
                name: "reviewed_at_utc",
                table: "inbox_emails");
        }
    }
}
