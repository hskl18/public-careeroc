using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Careeroc.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddResumeIntelligence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "resume_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    source_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
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
                name: "resume_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    resume_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    skills_json = table.Column<string>(type: "jsonb", nullable: false),
                    project_highlights_json = table.Column<string>(type: "jsonb", nullable: false),
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
                name: "resume_corrections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    resume_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_path = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    previous_value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    corrected_value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resume_corrections", x => x.id);
                    table.ForeignKey(
                        name: "fk_resume_corrections_resume_versions_resume_version_id",
                        column: x => x.resume_version_id,
                        principalTable: "resume_versions",
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
                    verdict = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    strengths_json = table.Column<string>(type: "jsonb", nullable: false),
                    gaps_json = table.Column<string>(type: "jsonb", nullable: false),
                    next_actions_json = table.Column<string>(type: "jsonb", nullable: false),
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
                name: "ix_resume_corrections_resume_version_id_created_at_utc",
                table: "resume_corrections",
                columns: new[] { "resume_version_id", "created_at_utc" });

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "resume_corrections");

            migrationBuilder.DropTable(
                name: "resume_evaluations");

            migrationBuilder.DropTable(
                name: "resume_versions");

            migrationBuilder.DropTable(
                name: "resume_documents");
        }
    }
}
