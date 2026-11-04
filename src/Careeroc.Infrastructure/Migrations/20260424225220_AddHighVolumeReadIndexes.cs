using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Careeroc.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHighVolumeReadIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_job_application_events_job_application_id",
                table: "job_application_events");

            migrationBuilder.DropIndex(
                name: "ix_inbox_emails_job_application_id",
                table: "inbox_emails");

            migrationBuilder.CreateIndex(
                name: "ix_reminders_status_due_at_utc",
                table: "reminders",
                columns: new[] { "status", "due_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_job_applications_user_id_last_activity_at_utc",
                table: "job_applications",
                columns: new[] { "user_id", "last_activity_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_job_application_events_job_application_id_event_time_utc",
                table: "job_application_events",
                columns: new[] { "job_application_id", "event_time_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_inbox_emails_inbox_account_id_received_at_utc",
                table: "inbox_emails",
                columns: new[] { "inbox_account_id", "received_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_inbox_emails_inbox_account_id_thread_id_received_at_utc",
                table: "inbox_emails",
                columns: new[] { "inbox_account_id", "thread_id", "received_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_inbox_emails_job_application_id_received_at_utc",
                table: "inbox_emails",
                columns: new[] { "job_application_id", "received_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reminders_status_due_at_utc",
                table: "reminders");

            migrationBuilder.DropIndex(
                name: "ix_job_applications_user_id_last_activity_at_utc",
                table: "job_applications");

            migrationBuilder.DropIndex(
                name: "ix_job_application_events_job_application_id_event_time_utc",
                table: "job_application_events");

            migrationBuilder.DropIndex(
                name: "ix_inbox_emails_inbox_account_id_received_at_utc",
                table: "inbox_emails");

            migrationBuilder.DropIndex(
                name: "ix_inbox_emails_inbox_account_id_thread_id_received_at_utc",
                table: "inbox_emails");

            migrationBuilder.DropIndex(
                name: "ix_inbox_emails_job_application_id_received_at_utc",
                table: "inbox_emails");

            migrationBuilder.CreateIndex(
                name: "ix_job_application_events_job_application_id",
                table: "job_application_events",
                column: "job_application_id");

            migrationBuilder.CreateIndex(
                name: "ix_inbox_emails_job_application_id",
                table: "inbox_emails",
                column: "job_application_id");
        }
    }
}
