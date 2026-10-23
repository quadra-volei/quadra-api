using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Quadra.Modules.Geo.Migrations
{
    /// <inheritdoc />
    public partial class CreateArenas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "arenas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    location = table.Column<Point>(type: "geography (point, 4326)", nullable: false),
                    kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Draft"),
                    merged_into_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_arenas", x => x.id);
                    table.CheckConstraint("ck_arenas_kind", "kind IN ('Court', 'Sand', 'Park')");
                    table.CheckConstraint("ck_arenas_merged_into", "((status = 'Merged') = (merged_into_id IS NOT NULL)) AND merged_into_id IS DISTINCT FROM id");
                    table.CheckConstraint("ck_arenas_reviewed", "status = 'Draft' OR (reviewed_by IS NOT NULL AND reviewed_at IS NOT NULL)");
                    table.CheckConstraint("ck_arenas_status", "status IN ('Draft', 'Active', 'Inactive', 'Merged')");
                    table.ForeignKey(
                        name: "fk_arenas_arenas_merged_into_id",
                        column: x => x.merged_into_id,
                        principalTable: "arenas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "arena_places",
                columns: table => new
                {
                    place_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    arena_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_arena_places", x => x.place_id);
                    table.CheckConstraint("ck_arena_places_primary", "NOT is_primary OR status = 'Linked'");
                    table.CheckConstraint("ck_arena_places_status", "(status = 'Linked' AND arena_id IS NOT NULL) OR (status = 'Rejected' AND arena_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_arena_places_arenas_arena_id",
                        column: x => x.arena_id,
                        principalTable: "arenas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_arena_places_arena_id",
                table: "arena_places",
                column: "arena_id");

            migrationBuilder.CreateIndex(
                name: "uq_arena_places_primary",
                table: "arena_places",
                column: "arena_id",
                unique: true,
                filter: "is_primary");

            migrationBuilder.CreateIndex(
                name: "ix_arenas_location_gist",
                table: "arenas",
                column: "location")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_arenas_merged_into_id",
                table: "arenas",
                column: "merged_into_id");

            migrationBuilder.CreateIndex(
                name: "ix_arenas_status",
                table: "arenas",
                column: "status");

            // The game↔arena link: Matches owns the column (AddMatchArena), the key lives here
            // because Geo migrates after Matches, so both tables exist by now. An arena with games
            // cannot be deleted, so every arena_id points at a real arena.
            migrationBuilder.Sql(
                "ALTER TABLE matches ADD CONSTRAINT fk_matches_arenas_arena_id " +
                "FOREIGN KEY (arena_id) REFERENCES arenas (id) ON DELETE RESTRICT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE matches DROP CONSTRAINT IF EXISTS fk_matches_arenas_arena_id;");

            migrationBuilder.DropTable(
                name: "arena_places");

            migrationBuilder.DropTable(
                name: "arenas");
        }
    }
}
