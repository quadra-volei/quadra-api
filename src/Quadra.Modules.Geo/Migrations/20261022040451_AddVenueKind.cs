using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quadra.Modules.Geo.Migrations
{
    /// <inheritdoc />
    public partial class AddVenueKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "venues",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "Court");

            // Areas searched before kinds existed are searched again, so their arenas get one
            // (DECISIONS #87). Cached places cost no Google lookup, only the area searches.
            migrationBuilder.Sql("DELETE FROM venue_areas;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "kind",
                table: "venues");
        }
    }
}
