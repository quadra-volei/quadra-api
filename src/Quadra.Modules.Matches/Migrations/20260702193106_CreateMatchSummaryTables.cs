using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quadra.Modules.Matches.Migrations
{
    /// <inheritdoc />
    public partial class CreateMatchSummaryTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "match_summaries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    format = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    team_a_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_b_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_a_sets_won = table.Column<short>(type: "smallint", nullable: false),
                    team_b_sets_won = table.Column<short>(type: "smallint", nullable: false),
                    winner_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    mvp_player_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mvp_vote_count = table.Column<short>(type: "smallint", nullable: true),
                    mvp_total_votes = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    generated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_match_summaries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "match_summary_players",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    match_summary_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_match_summary_players", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "match_summary_sets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    match_summary_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    set_number = table.Column<short>(type: "smallint", nullable: false),
                    team_a_points = table.Column<short>(type: "smallint", nullable: false),
                    team_b_points = table.Column<short>(type: "smallint", nullable: false),
                    winner_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_match_summary_sets", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "uq_match_summaries_match_id",
                table: "match_summaries",
                column: "match_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_match_summary_players_match_id",
                table: "match_summary_players",
                column: "match_id");

            migrationBuilder.CreateIndex(
                name: "ix_match_summary_players_match_summary_id",
                table: "match_summary_players",
                column: "match_summary_id");

            migrationBuilder.CreateIndex(
                name: "uq_match_summary_players_summary_player",
                table: "match_summary_players",
                columns: new[] { "match_summary_id", "player_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_match_summary_sets_match_id",
                table: "match_summary_sets",
                column: "match_id");

            migrationBuilder.CreateIndex(
                name: "ix_match_summary_sets_match_summary_id",
                table: "match_summary_sets",
                column: "match_summary_id");

            migrationBuilder.CreateIndex(
                name: "uq_match_summary_sets_summary_set_number",
                table: "match_summary_sets",
                columns: new[] { "match_summary_id", "set_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "match_summaries");

            migrationBuilder.DropTable(
                name: "match_summary_players");

            migrationBuilder.DropTable(
                name: "match_summary_sets");
        }
    }
}
