using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quadra.Modules.Profile.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchHistoryActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "aces",
                table: "player_match_history",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "assists",
                table: "player_match_history",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "blocks",
                table: "player_match_history",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "digs",
                table: "player_match_history",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "exposure_games",
                table: "player_match_history",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "kills",
                table: "player_match_history",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "aces",
                table: "player_match_history");

            migrationBuilder.DropColumn(
                name: "assists",
                table: "player_match_history");

            migrationBuilder.DropColumn(
                name: "blocks",
                table: "player_match_history");

            migrationBuilder.DropColumn(
                name: "digs",
                table: "player_match_history");

            migrationBuilder.DropColumn(
                name: "exposure_games",
                table: "player_match_history");

            migrationBuilder.DropColumn(
                name: "kills",
                table: "player_match_history");
        }
    }
}
