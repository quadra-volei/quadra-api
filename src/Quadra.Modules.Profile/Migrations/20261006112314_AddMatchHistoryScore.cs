using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quadra.Modules.Profile.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchHistoryScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "format",
                table: "player_match_history",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sets_lost",
                table: "player_match_history",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sets_won",
                table: "player_match_history",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "format",
                table: "player_match_history");

            migrationBuilder.DropColumn(
                name: "sets_lost",
                table: "player_match_history");

            migrationBuilder.DropColumn(
                name: "sets_won",
                table: "player_match_history");
        }
    }
}
