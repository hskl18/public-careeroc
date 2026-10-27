using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Careeroc.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserMemoryAndProfileEvaluation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_memory_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
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
                name: "user_soul_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    narrative_tone = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    career_narrative = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    communication_preferences_json = table.Column<string>(type: "jsonb", nullable: true),
                    rendered_soul_markdown = table.Column<string>(type: "text", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_soul_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_soul_profiles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "ix_user_soul_profiles_user_id",
                table: "user_soul_profiles",
                column: "user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_memory_entries");

            migrationBuilder.DropTable(
                name: "user_soul_profiles");
        }
    }
}
