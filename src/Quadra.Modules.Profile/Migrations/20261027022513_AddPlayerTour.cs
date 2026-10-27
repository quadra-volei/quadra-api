using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quadra.Modules.Profile.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerTour : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "tour_ended_at",
                table: "player_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "tour_last_step",
                table: "player_profiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tour_outcome",
                table: "player_profiles",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "tour_version",
                table: "player_profiles",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tour_ended_at",
                table: "player_profiles");

            migrationBuilder.DropColumn(
                name: "tour_last_step",
                table: "player_profiles");

            migrationBuilder.DropColumn(
                name: "tour_outcome",
                table: "player_profiles");

            migrationBuilder.DropColumn(
                name: "tour_version",
                table: "player_profiles");
        }
    }
}
