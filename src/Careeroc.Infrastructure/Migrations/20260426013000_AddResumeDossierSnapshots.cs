using Careeroc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Careeroc.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(CareerocDbContext))]
    [Migration("20260426013000_AddResumeDossierSnapshots")]
    public partial class AddResumeDossierSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_file_name",
                table: "resume_documents",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "structured_data_json",
                table: "resume_versions",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "extraction_metadata_json",
                table: "resume_versions",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "evaluation_run_id",
                table: "resume_versions",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "headline",
                table: "resume_evaluations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "section_reviews_json",
                table: "resume_evaluations",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "source_file_name",
                table: "resume_documents");

            migrationBuilder.DropColumn(
                name: "structured_data_json",
                table: "resume_versions");

            migrationBuilder.DropColumn(
                name: "extraction_metadata_json",
                table: "resume_versions");

            migrationBuilder.DropColumn(
                name: "evaluation_run_id",
                table: "resume_versions");

            migrationBuilder.DropColumn(
                name: "headline",
                table: "resume_evaluations");

            migrationBuilder.DropColumn(
                name: "section_reviews_json",
                table: "resume_evaluations");
        }
    }
}
