using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Careeroc.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRecruitingContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "recruiting_contact_id",
                table: "inbox_emails",
                type: "uuid",
                nullable: true);

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

            migrationBuilder.CreateIndex(
                name: "ix_inbox_emails_recruiting_contact_id_received_at_utc",
                table: "inbox_emails",
                columns: new[] { "recruiting_contact_id", "received_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_recruiting_contacts_user_id_email",
                table: "recruiting_contacts",
                columns: new[] { "user_id", "email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recruiting_contacts_user_id_last_seen_at_utc",
                table: "recruiting_contacts",
                columns: new[] { "user_id", "last_seen_at_utc" });

            migrationBuilder.AddForeignKey(
                name: "fk_inbox_emails_recruiting_contacts_recruiting_contact_id",
                table: "inbox_emails",
                column: "recruiting_contact_id",
                principalTable: "recruiting_contacts",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_inbox_emails_recruiting_contacts_recruiting_contact_id",
                table: "inbox_emails");

            migrationBuilder.DropTable(
                name: "recruiting_contacts");

            migrationBuilder.DropIndex(
                name: "ix_inbox_emails_recruiting_contact_id_received_at_utc",
                table: "inbox_emails");

            migrationBuilder.DropColumn(
                name: "recruiting_contact_id",
                table: "inbox_emails");
        }
    }
}
