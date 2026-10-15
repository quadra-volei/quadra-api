using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quadra.Modules.Profile.Migrations
{
    /// <inheritdoc />
    public partial class AddActivities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "share_activity",
                table: "player_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: true); // sharing is on by default, also for existing players

            migrationBuilder.CreateTable(
                name: "activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: true),
                    match_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    outcome = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    sets_won = table.Column<int>(type: "integer", nullable: true),
                    sets_lost = table.Column<int>(type: "integer", nullable: true),
                    level_from = table.Column<int>(type: "integer", nullable: true),
                    level_to = table.Column<int>(type: "integer", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dedup_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activities", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activities_actor_id_occurred_at",
                table: "activities",
                columns: new[] { "actor_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ux_activities_actor_id_dedup_key",
                table: "activities",
                columns: new[] { "actor_id", "dedup_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activities");

            migrationBuilder.DropColumn(
                name: "share_activity",
                table: "player_profiles");
        }
    }
}
