using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quadra.Modules.Matches.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchSettingsAndGuests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "duration_minutes",
                table: "matches",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "format",
                table: "matches",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "invite_code",
                table: "matches",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "invite_mode",
                table: "matches",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "level",
                table: "matches",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "price_monthly",
                table: "matches",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<int[]>(
                name: "recurrence_days",
                table: "matches",
                type: "integer[]",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "visibility",
                table: "matches",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Open");

            migrationBuilder.CreateTable(
                name: "match_guests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    position = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    added_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_match_guests", x => x.id);
                    table.ForeignKey(
                        name: "FK_match_guests_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_match_guests_match_id",
                table: "match_guests",
                column: "match_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "match_guests");

            migrationBuilder.DropColumn(
                name: "duration_minutes",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "format",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "invite_code",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "invite_mode",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "level",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "price_monthly",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "recurrence_days",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "visibility",
                table: "matches");
        }
    }
}
