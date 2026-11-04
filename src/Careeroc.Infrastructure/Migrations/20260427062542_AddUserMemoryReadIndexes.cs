using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Careeroc.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserMemoryReadIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_user_memory_entries_user_status_exp_updated",
                table: "user_memory_entries",
                columns: new[] { "user_id", "confirmation_status", "expires_at_utc", "updated_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_user_memory_entries_user_kind_key_status",
                table: "user_memory_entries",
                columns: new[] { "user_id", "kind", "key", "confirmation_status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_user_memory_entries_user_status_exp_updated",
                table: "user_memory_entries");

            migrationBuilder.DropIndex(
                name: "ix_user_memory_entries_user_kind_key_status",
                table: "user_memory_entries");
        }
    }
}
