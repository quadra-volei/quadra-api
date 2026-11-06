using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quadra.Modules.Geo.Migrations
{
    /// <inheritdoc />
    public partial class AddArenaLocationSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "location_source",
                table: "arenas",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Curator");

            migrationBuilder.AddCheckConstraint(
                name: "ck_arenas_location_source",
                table: "arenas",
                sql: "location_source IN ('Curator', 'Candidate')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_arenas_location_source",
                table: "arenas");

            migrationBuilder.DropColumn(
                name: "location_source",
                table: "arenas");
        }
    }
}
