using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Careeroc.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SplitInboxAccountTokensFromMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "access_token_encrypted",
                table: "inbox_accounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refresh_token_encrypted",
                table: "inbox_accounts",
                type: "text",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE inbox_accounts
                SET
                    access_token_encrypted = NULLIF(o_auth_metadata_json ->> 'accessToken', ''),
                    refresh_token_encrypted = NULLIF(o_auth_metadata_json ->> 'refreshToken', ''),
                    o_auth_metadata_json = o_auth_metadata_json - 'accessToken' - 'refreshToken'
                WHERE o_auth_metadata_json IS NOT NULL
                  AND jsonb_typeof(o_auth_metadata_json) = 'object'
                  AND (o_auth_metadata_json ? 'accessToken' OR o_auth_metadata_json ? 'refreshToken');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE inbox_accounts
                SET o_auth_metadata_json =
                    COALESCE(o_auth_metadata_json, '{}'::jsonb) ||
                    jsonb_strip_nulls(
                        jsonb_build_object(
                            'accessToken', access_token_encrypted,
                            'refreshToken', refresh_token_encrypted))
                WHERE access_token_encrypted IS NOT NULL
                   OR refresh_token_encrypted IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "access_token_encrypted",
                table: "inbox_accounts");

            migrationBuilder.DropColumn(
                name: "refresh_token_encrypted",
                table: "inbox_accounts");
        }
    }
}
