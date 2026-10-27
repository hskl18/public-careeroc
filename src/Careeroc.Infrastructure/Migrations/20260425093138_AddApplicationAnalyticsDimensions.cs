using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Careeroc.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationAnalyticsDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "location",
                table: "job_applications",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "role_canonical",
                table: "job_applications",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "seniority_level",
                table: "job_applications",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_system",
                table: "job_applications",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_applications_user_id_role_canonical",
                table: "job_applications",
                columns: new[] { "user_id", "role_canonical" });

            migrationBuilder.CreateIndex(
                name: "ix_job_applications_user_id_source_system",
                table: "job_applications",
                columns: new[] { "user_id", "source_system" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_job_applications_user_id_role_canonical",
                table: "job_applications");

            migrationBuilder.DropIndex(
                name: "ix_job_applications_user_id_source_system",
                table: "job_applications");

            migrationBuilder.DropColumn(
                name: "location",
                table: "job_applications");

            migrationBuilder.DropColumn(
                name: "role_canonical",
                table: "job_applications");

            migrationBuilder.DropColumn(
                name: "seniority_level",
                table: "job_applications");

            migrationBuilder.DropColumn(
                name: "source_system",
                table: "job_applications");
        }
    }
}
