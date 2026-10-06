using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quadra.Modules.InGame.Migrations
{
    /// <inheritdoc />
    public partial class CreateScoreboardTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scoreboard_sets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    scoreboard_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    set_number = table.Column<short>(type: "smallint", nullable: false),
                    team_a_points = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    team_b_points = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "InProgress"),
                    is_deciding_set = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    winner_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scoreboard_sets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scoreboards",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    format = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "NotStarted"),
                    team_a_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_b_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_a_sets_won = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    team_b_sets_won = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    current_set_number = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    winner_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scoreboards", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_scoreboard_sets_match_id",
                table: "scoreboard_sets",
                column: "match_id");

            migrationBuilder.CreateIndex(
                name: "ix_scoreboard_sets_scoreboard_id",
                table: "scoreboard_sets",
                column: "scoreboard_id");

            migrationBuilder.CreateIndex(
                name: "uq_scoreboard_sets_scoreboard_set_number",
                table: "scoreboard_sets",
                columns: new[] { "scoreboard_id", "set_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_scoreboards_match_id",
                table: "scoreboards",
                column: "match_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scoreboard_sets");

            migrationBuilder.DropTable(
                name: "scoreboards");
        }
    }
}
