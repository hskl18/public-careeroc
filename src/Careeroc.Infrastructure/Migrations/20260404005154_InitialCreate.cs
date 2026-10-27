using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Careeroc.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    email_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    o_auth_metadata_json = table.Column<string>(type: "jsonb", nullable: true),
                    last_synced_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_inbox_accounts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    role = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    last_activity_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_applications", x => x.id);
                    table.ForeignKey(
                        name: "fk_job_applications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inbox_emails",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    inbox_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_application_id = table.Column<Guid>(type: "uuid", nullable: true),
                    external_message_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    thread_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sender = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    raw_body = table.Column<string>(type: "text", nullable: false),
                    parsed_body = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<int>(type: "integer", nullable: true),
                    action_required = table.Column<bool>(type: "boolean", nullable: false),
                    due_date_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    extracted_company = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    extracted_role = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    processing_notes = table.Column<string>(type: "text", nullable: true),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_emails", x => x.id);
                    table.ForeignKey(
                        name: "fk_inbox_emails_inbox_accounts_inbox_account_id",
                        column: x => x.inbox_account_id,
                        principalTable: "inbox_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_inbox_emails_job_applications_job_application_id",
                        column: x => x.job_application_id,
                        principalTable: "job_applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "reminders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    due_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reminders", x => x.id);
                    table.ForeignKey(
                        name: "fk_reminders_job_applications_job_application_id",
                        column: x => x.job_application_id,
                        principalTable: "job_applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "email_processing_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    inbox_email_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    available_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    locked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_processing_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_email_processing_jobs_inbox_emails_inbox_email_id",
                        column: x => x.inbox_email_id,
                        principalTable: "inbox_emails",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_application_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inbox_email_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    event_time_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    extracted_data_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_application_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_job_application_events_inbox_emails_inbox_email_id",
                        column: x => x.inbox_email_id,
                        principalTable: "inbox_emails",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_job_application_events_job_applications_job_application_id",
                        column: x => x.job_application_id,
                        principalTable: "job_applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_processing_jobs_inbox_email_id",
                table: "email_processing_jobs",
                column: "inbox_email_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_email_processing_jobs_status_available_at_utc",
                table: "email_processing_jobs",
                columns: new[] { "status", "available_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_inbox_accounts_user_id_email_address",
                table: "inbox_accounts",
                columns: new[] { "user_id", "email_address" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inbox_emails_inbox_account_id_external_message_id",
                table: "inbox_emails",
                columns: new[] { "inbox_account_id", "external_message_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inbox_emails_job_application_id",
                table: "inbox_emails",
                column: "job_application_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_application_events_inbox_email_id",
                table: "job_application_events",
                column: "inbox_email_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_application_events_job_application_id",
                table: "job_application_events",
                column: "job_application_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_applications_user_id_company_role",
                table: "job_applications",
                columns: new[] { "user_id", "company", "role" });

            migrationBuilder.CreateIndex(
                name: "ix_reminders_job_application_id",
                table: "reminders",
                column: "job_application_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_processing_jobs");

            migrationBuilder.DropTable(
                name: "job_application_events");

            migrationBuilder.DropTable(
                name: "reminders");

            migrationBuilder.DropTable(
                name: "inbox_emails");

            migrationBuilder.DropTable(
                name: "inbox_accounts");

            migrationBuilder.DropTable(
                name: "job_applications");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
