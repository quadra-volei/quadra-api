using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quadra.Modules.InGame.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerMatchStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "player_match_stats",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kills = table.Column<int>(type: "integer", nullable: false),
                    blocks = table.Column<int>(type: "integer", nullable: false),
                    aces = table.Column<int>(type: "integer", nullable: false),
                    digs = table.Column<int>(type: "integer", nullable: false),
                    assists = table.Column<int>(type: "integer", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_match_stats", x => x.id);
                    table.CheckConstraint("ck_player_match_stats_aces", "aces BETWEEN 0 AND 99");
                    table.CheckConstraint("ck_player_match_stats_assists", "assists BETWEEN 0 AND 99");
                    table.CheckConstraint("ck_player_match_stats_blocks", "blocks BETWEEN 0 AND 99");
                    table.CheckConstraint("ck_player_match_stats_digs", "digs BETWEEN 0 AND 99");
                    table.CheckConstraint("ck_player_match_stats_kills", "kills BETWEEN 0 AND 99");
                });

            migrationBuilder.CreateIndex(
                name: "uq_player_match_stats_match_player",
                table: "player_match_stats",
                columns: new[] { "match_id", "player_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "player_match_stats");
        }
    }
}
