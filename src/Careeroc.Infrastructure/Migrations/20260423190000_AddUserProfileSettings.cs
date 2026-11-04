using Careeroc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Careeroc.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(CareerocDbContext))]
    [Migration("20260423190000_AddUserProfileSettings")]
    public partial class AddUserProfileSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE users
                    ADD COLUMN IF NOT EXISTS time_zone_id character varying(80) NOT NULL DEFAULT 'America/Los_Angeles',
                    ADD COLUMN IF NOT EXISTS location character varying(160),
                    ADD COLUMN IF NOT EXISTS target_role character varying(200);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE users
                    DROP COLUMN IF EXISTS time_zone_id,
                    DROP COLUMN IF EXISTS location,
                    DROP COLUMN IF EXISTS target_role;
                """);
        }
    }
}
