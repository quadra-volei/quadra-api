using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quadra.Modules.Matches.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchGenderSurfaceAndPrivateCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "gender",
                table: "matches",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "surface",
                table: "matches",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            // Every private match is now joined by its invite code: the "Guests" ones (and any
            // without a code) get one. 8 characters from md5 hex with 0/1 swapped for letters,
            // so no look-alikes, as GenerateInviteCode.
            migrationBuilder.Sql(
                """
                UPDATE matches
                SET invite_mode = 'Code',
                    invite_code = COALESCE(invite_code,
                        upper(substr(translate(md5(random()::text || id::text), '01', 'gh'), 1, 8)))
                WHERE visibility = 'Private';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "gender",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "surface",
                table: "matches");
        }
    }
}
