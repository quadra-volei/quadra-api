using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quadra.Modules.InGame.Migrations
{
    /// <inheritdoc />
    public partial class CreateMvpVotingTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mvp_votes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    voting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    voter_player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    voted_player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mvp_votes", x => x.id);
                    table.CheckConstraint("ck_mvp_votes_no_self_vote", "voter_player_id <> voted_player_id");
                });

            migrationBuilder.CreateTable(
                name: "mvp_votings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Open"),
                    deadline_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    total_votes = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    mvp_player_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mvp_vote_count = table.Column<short>(type: "smallint", nullable: true),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mvp_votings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mvp_votes_match_voted",
                table: "mvp_votes",
                columns: new[] { "match_id", "voted_player_id" });

            migrationBuilder.CreateIndex(
                name: "ix_mvp_votes_voting_id",
                table: "mvp_votes",
                column: "voting_id");

            migrationBuilder.CreateIndex(
                name: "uq_mvp_votes_match_voter",
                table: "mvp_votes",
                columns: new[] { "match_id", "voter_player_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mvp_votings_state_deadline_at",
                table: "mvp_votings",
                columns: new[] { "state", "deadline_at" });

            migrationBuilder.CreateIndex(
                name: "uq_mvp_votings_match_id",
                table: "mvp_votings",
                column: "match_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mvp_votes");

            migrationBuilder.DropTable(
                name: "mvp_votings");
        }
    }
}
