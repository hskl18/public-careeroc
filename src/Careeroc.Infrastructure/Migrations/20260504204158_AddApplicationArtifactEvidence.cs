using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Careeroc.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationArtifactEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "application_artifact_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    artifact_kind = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    file_name = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_label = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    safe_title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    extracted_fact_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    extracted_fact_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    normalized_date_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    source_snippet = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    model_path = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    purpose = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    evidence_source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    review_gate_result = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    fallback_path = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    requires_review = table.Column<bool>(type: "boolean", nullable: false),
                    review_reason = table.Column<string>(type: "character varying(280)", maxLength: 280, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_artifact_evidence", x => x.id);
                    table.ForeignKey(
                        name: "fk_application_artifact_evidence_job_applications_job_applicat",
                        column: x => x.job_application_id,
                        principalTable: "job_applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_application_artifact_evidence_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_application_artifact_evidence_job_application_id_created_at",
                table: "application_artifact_evidence",
                columns: new[] { "job_application_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_application_artifact_evidence_user_id_created_at_utc",
                table: "application_artifact_evidence",
                columns: new[] { "user_id", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "application_artifact_evidence");
        }
    }
}
