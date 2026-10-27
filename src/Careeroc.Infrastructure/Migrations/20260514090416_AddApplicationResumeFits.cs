using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Careeroc.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationResumeFits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "application_resume_fits");
        }
    }
}
