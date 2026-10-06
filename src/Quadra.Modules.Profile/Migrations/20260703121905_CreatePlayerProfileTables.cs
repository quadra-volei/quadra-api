using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quadra.Modules.Profile.Migrations
{
    /// <inheritdoc />
    public partial class CreatePlayerProfileTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "player_match_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    match_date_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    outcome = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    was_mvp = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    duration_seconds = table.Column<int>(type: "integer", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_match_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "player_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    primary_position = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    secondary_position = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    photo_object_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Beginner"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "player_stats",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    matches_played = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    wins = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    losses = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    draws = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    mvps_received = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_stats", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_player_match_history_match_id",
                table: "player_match_history",
                column: "match_id");

            migrationBuilder.CreateIndex(
                name: "ix_player_match_history_user_date",
                table: "player_match_history",
                columns: new[] { "user_id", "match_date_time" });

            migrationBuilder.CreateIndex(
                name: "uq_player_match_history_user_match",
                table: "player_match_history",
                columns: new[] { "user_id", "match_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_player_profiles_user_id",
                table: "player_profiles",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_player_stats_user_id",
                table: "player_stats",
                column: "user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "player_match_history");

            migrationBuilder.DropTable(
                name: "player_profiles");

            migrationBuilder.DropTable(
                name: "player_stats");
        }
    }
}
