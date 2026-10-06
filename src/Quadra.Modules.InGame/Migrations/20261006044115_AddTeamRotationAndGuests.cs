using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quadra.Modules.InGame.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamRotationAndGuests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_guest",
                table: "team_members",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "rotates_teams",
                table: "scoreboards",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "last_point_team_id",
                table: "scoreboard_sets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "team_a_id",
                table: "scoreboard_sets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "team_b_id",
                table: "scoreboard_sets",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_guest",
                table: "team_members");

            migrationBuilder.DropColumn(
                name: "rotates_teams",
                table: "scoreboards");

            migrationBuilder.DropColumn(
                name: "last_point_team_id",
                table: "scoreboard_sets");

            migrationBuilder.DropColumn(
                name: "team_a_id",
                table: "scoreboard_sets");

            migrationBuilder.DropColumn(
                name: "team_b_id",
                table: "scoreboard_sets");
        }
    }
}
