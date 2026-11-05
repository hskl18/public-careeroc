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
                    correlation_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
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

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    profile_icon = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, defaultValue: "inbox-blue"),
                    location = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    target_role = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    welcome_dismissed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    access_token_encrypted = table.Column<string>(type: "text", nullable: true),
                    refresh_token_encrypted = table.Column<string>(type: "text", nullable: true),
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
                    role_canonical = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_system = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    location = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    seniority_level = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
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
                name: "personal_access_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    token_prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scope = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_personal_access_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_personal_access_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recruiting_contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    company_hint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    role_hint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    contact_type = table.Column<int>(type: "integer", nullable: true),
                    confidence = table.Column<decimal>(type: "numeric(4,2)", nullable: true),
                    message_count = table.Column<int>(type: "integer", nullable: false),
                    first_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recruiting_contacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_recruiting_contacts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "resume_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    source_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source_file_name = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    source_text = table.Column<string>(type: "text", nullable: false),
                    source_links_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resume_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_resume_documents_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_memory_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    value = table.Column<string>(type: "character varying(32000)", maxLength: 32000, nullable: false),
                    summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    source_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    confidence = table.Column<double>(type: "double precision", nullable: false),
                    confirmation_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_memory_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_memory_entries_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    evidence_refreshed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
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
                name: "inbox_emails",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    inbox_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_application_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recruiting_contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    external_message_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    thread_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sender = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    raw_body = table.Column<string>(type: "text", nullable: false),
                    parsed_body = table.Column<string>(type: "text", nullable: false),
                    is_outbound = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    category = table.Column<int>(type: "integer", nullable: true),
                    contact_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    contact_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    contact_role_hint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    contact_type = table.Column<int>(type: "integer", nullable: true),
                    action_required = table.Column<bool>(type: "boolean", nullable: false),
                    due_date_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    extracted_company = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    extracted_role = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    classification_confidence = table.Column<decimal>(type: "numeric(4,2)", nullable: true),
                    matching_confidence = table.Column<decimal>(type: "numeric(4,2)", nullable: true),
                    requires_manual_review = table.Column<bool>(type: "boolean", nullable: false),
                    review_reason = table.Column<string>(type: "text", nullable: true),
                    reviewed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    processing_notes = table.Column<string>(type: "text", nullable: true),
                    processing_trace_json = table.Column<string>(type: "jsonb", nullable: true),
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
                    table.ForeignKey(
                        name: "fk_inbox_emails_recruiting_contacts_recruiting_contact_id",
                        column: x => x.recruiting_contact_id,
                        principalTable: "recruiting_contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "resume_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    resume_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    skills_json = table.Column<string>(type: "jsonb", nullable: false),
                    project_highlights_json = table.Column<string>(type: "jsonb", nullable: false),
                    structured_data_json = table.Column<string>(type: "jsonb", nullable: false),
                    extraction_metadata_json = table.Column<string>(type: "jsonb", nullable: false),
                    experience_count = table.Column<int>(type: "integer", nullable: false),
                    project_count = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resume_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_resume_versions_resume_documents_resume_document_id",
                        column: x => x.resume_document_id,
                        principalTable: "resume_documents",
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
                    run_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    dead_letter_reason = table.Column<string>(type: "character varying(280)", maxLength: 280, nullable: true),
                    dead_lettered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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

            migrationBuilder.CreateTable(
                name: "application_resume_fits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resume_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_score = table.Column<int>(type: "integer", nullable: false),
                    strengths_json = table.Column<string>(type: "jsonb", nullable: false),
                    gaps_json = table.Column<string>(type: "jsonb", nullable: false),
                    rationale = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_resume_fits", x => x.id);
                    table.ForeignKey(
                        name: "fk_application_resume_fits_job_applications_job_application_id",
                        column: x => x.job_application_id,
                        principalTable: "job_applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_application_resume_fits_resume_versions_resume_version_id",
                        column: x => x.resume_version_id,
                        principalTable: "resume_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_application_resume_fits_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "resume_evaluations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    resume_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    headline = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    verdict = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    strengths_json = table.Column<string>(type: "jsonb", nullable: false),
                    gaps_json = table.Column<string>(type: "jsonb", nullable: false),
                    next_actions_json = table.Column<string>(type: "jsonb", nullable: false),
                    section_reviews_json = table.Column<string>(type: "jsonb", nullable: false),
                    evidence_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resume_evaluations", x => x.id);
                    table.ForeignKey(
                        name: "fk_resume_evaluations_resume_versions_resume_version_id",
                        column: x => x.resume_version_id,
                        principalTable: "resume_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_application_resume_fits_job_application_id_resume_version_id",
                table: "application_resume_fits",
                columns: new[] { "job_application_id", "resume_version_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_application_resume_fits_resume_version_id",
                table: "application_resume_fits",
                column: "resume_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_application_resume_fits_user_id_updated_at_utc",
                table: "application_resume_fits",
                columns: new[] { "user_id", "updated_at_utc" });

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

            migrationBuilder.CreateIndex(
                name: "ix_email_processing_jobs_correlation_id",
                table: "email_processing_jobs",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_email_processing_jobs_inbox_email_id",
                table: "email_processing_jobs",
                column: "inbox_email_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_email_processing_jobs_run_id",
                table: "email_processing_jobs",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_email_processing_jobs_status_available_at_utc",
                table: "email_processing_jobs",
                columns: new[] { "status", "available_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_email_processing_jobs_status_dead_lettered_at_utc",
                table: "email_processing_jobs",
                columns: new[] { "status", "dead_lettered_at_utc" });

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

            migrationBuilder.CreateIndex(
                name: "ix_inbox_emails_recruiting_contact_id_received_at_utc",
                table: "inbox_emails",
                columns: new[] { "recruiting_contact_id", "received_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_inbox_emails_requires_manual_review_received_at_utc",
                table: "inbox_emails",
                columns: new[] { "requires_manual_review", "received_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_job_application_events_inbox_email_id",
                table: "job_application_events",
                column: "inbox_email_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_application_events_job_application_id_event_time_utc",
                table: "job_application_events",
                columns: new[] { "job_application_id", "event_time_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_job_applications_user_id_company_role",
                table: "job_applications",
                columns: new[] { "user_id", "company", "role" });

            migrationBuilder.CreateIndex(
                name: "ix_job_applications_user_id_last_activity_at_utc",
                table: "job_applications",
                columns: new[] { "user_id", "last_activity_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_job_applications_user_id_role_canonical",
                table: "job_applications",
                columns: new[] { "user_id", "role_canonical" });

            migrationBuilder.CreateIndex(
                name: "ix_job_applications_user_id_source_system",
                table: "job_applications",
                columns: new[] { "user_id", "source_system" });

            migrationBuilder.CreateIndex(
                name: "ix_model_call_events_correlation_id",
                table: "model_call_events",
                column: "correlation_id");

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

            migrationBuilder.CreateIndex(
                name: "ix_personal_access_tokens_token_hash",
                table: "personal_access_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_personal_access_tokens_user_id_created_at_utc",
                table: "personal_access_tokens",
                columns: new[] { "user_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_recruiting_contacts_user_id_email",
                table: "recruiting_contacts",
                columns: new[] { "user_id", "email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recruiting_contacts_user_id_last_seen_at_utc",
                table: "recruiting_contacts",
                columns: new[] { "user_id", "last_seen_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_reminders_job_application_id",
                table: "reminders",
                column: "job_application_id");

            migrationBuilder.CreateIndex(
                name: "ix_reminders_status_due_at_utc",
                table: "reminders",
                columns: new[] { "status", "due_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_resume_documents_user_id_updated_at_utc",
                table: "resume_documents",
                columns: new[] { "user_id", "updated_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_resume_evaluations_resume_version_id",
                table: "resume_evaluations",
                column: "resume_version_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_resume_versions_resume_document_id_created_at_utc",
                table: "resume_versions",
                columns: new[] { "resume_document_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_user_memory_entries_user_id_confirmation_status",
                table: "user_memory_entries",
                columns: new[] { "user_id", "confirmation_status" });

            migrationBuilder.CreateIndex(
                name: "ix_user_memory_entries_user_id_key",
                table: "user_memory_entries",
                columns: new[] { "user_id", "key" });

            migrationBuilder.CreateIndex(
                name: "ix_user_memory_entries_user_id_kind",
                table: "user_memory_entries",
                columns: new[] { "user_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "ix_user_memory_entries_user_kind_key_status",
                table: "user_memory_entries",
                columns: new[] { "user_id", "kind", "key", "confirmation_status" });

            migrationBuilder.CreateIndex(
                name: "ix_user_memory_entries_user_status_exp_updated",
                table: "user_memory_entries",
                columns: new[] { "user_id", "confirmation_status", "expires_at_utc", "updated_at_utc" });

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
                name: "application_resume_fits");

            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "email_processing_jobs");

            migrationBuilder.DropTable(
                name: "job_application_events");

            migrationBuilder.DropTable(
                name: "model_call_events");

            migrationBuilder.DropTable(
                name: "personal_access_tokens");

            migrationBuilder.DropTable(
                name: "reminders");

            migrationBuilder.DropTable(
                name: "resume_evaluations");

            migrationBuilder.DropTable(
                name: "user_memory_entries");

            migrationBuilder.DropTable(
                name: "inbox_emails");

            migrationBuilder.DropTable(
                name: "resume_versions");

            migrationBuilder.DropTable(
                name: "inbox_accounts");

            migrationBuilder.DropTable(
                name: "job_applications");

            migrationBuilder.DropTable(
                name: "recruiting_contacts");

            migrationBuilder.DropTable(
                name: "resume_documents");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
