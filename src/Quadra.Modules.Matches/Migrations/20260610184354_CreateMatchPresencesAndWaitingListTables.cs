using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quadra.Modules.Matches.Migrations
{
    /// <inheritdoc />
    public partial class CreateMatchPresencesAndWaitingListTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "released_drop_in_slots",
                table: "matches",
                type: "smallint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "match_presences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Pending"),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_match_presences", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "waiting_list",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_waiting_list", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_match_presences_match_id",
                table: "match_presences",
                column: "match_id");

            migrationBuilder.CreateIndex(
                name: "ix_match_presences_match_player_type",
                table: "match_presences",
                columns: new[] { "match_id", "player_type" });

            migrationBuilder.CreateIndex(
                name: "ix_match_presences_match_status",
                table: "match_presences",
                columns: new[] { "match_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_match_presences_player_id",
                table: "match_presences",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "uq_match_presences_match_player",
                table: "match_presences",
                columns: new[] { "match_id", "player_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_waiting_list_match_id",
                table: "waiting_list",
                column: "match_id");

            migrationBuilder.CreateIndex(
                name: "ix_waiting_list_match_position",
                table: "waiting_list",
                columns: new[] { "match_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_waiting_list_match_player",
                table: "waiting_list",
                columns: new[] { "match_id", "player_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "match_presences");

            migrationBuilder.DropTable(
                name: "waiting_list");

            migrationBuilder.DropColumn(
                name: "released_drop_in_slots",
                table: "matches");
        }
    }
}
