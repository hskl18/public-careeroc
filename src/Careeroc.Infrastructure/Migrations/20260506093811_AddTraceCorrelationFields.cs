using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Careeroc.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTraceCorrelationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "correlation_id",
                table: "model_call_events",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "correlation_id",
                table: "email_processing_jobs",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "run_id",
                table: "email_processing_jobs",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_model_call_events_correlation_id",
                table: "model_call_events",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_email_processing_jobs_correlation_id",
                table: "email_processing_jobs",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_email_processing_jobs_run_id",
                table: "email_processing_jobs",
                column: "run_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_model_call_events_correlation_id",
                table: "model_call_events");

            migrationBuilder.DropIndex(
                name: "ix_email_processing_jobs_correlation_id",
                table: "email_processing_jobs");

            migrationBuilder.DropIndex(
                name: "ix_email_processing_jobs_run_id",
                table: "email_processing_jobs");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                table: "model_call_events");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                table: "email_processing_jobs");

            migrationBuilder.DropColumn(
                name: "run_id",
                table: "email_processing_jobs");
        }
    }
}
