using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quadra.Modules.InGame.Migrations
{
    /// <inheritdoc />
    public partial class AddScoreboardPointsToWin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "points_to_win",
                table: "scoreboards",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "points_to_win",
                table: "scoreboards");
        }
    }
}
