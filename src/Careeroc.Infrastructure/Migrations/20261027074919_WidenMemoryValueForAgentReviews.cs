using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Careeroc.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WidenMemoryValueForAgentReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "value",
                table: "user_memory_entries",
                type: "character varying(32000)",
                maxLength: 32000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "value",
                table: "user_memory_entries",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32000)",
                oldMaxLength: 32000);
        }
    }
}
