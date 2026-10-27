using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Careeroc.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInboxEmailRecruitingContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "contact_email",
                table: "inbox_emails",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_name",
                table: "inbox_emails",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_role_hint",
                table: "inbox_emails",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "contact_type",
                table: "inbox_emails",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "contact_email",
                table: "inbox_emails");

            migrationBuilder.DropColumn(
                name: "contact_name",
                table: "inbox_emails");

            migrationBuilder.DropColumn(
                name: "contact_role_hint",
                table: "inbox_emails");

            migrationBuilder.DropColumn(
                name: "contact_type",
                table: "inbox_emails");
        }
    }
}
