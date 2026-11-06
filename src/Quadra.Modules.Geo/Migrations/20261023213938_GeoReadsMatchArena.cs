using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quadra.Modules.Geo.Migrations
{
    /// <inheritdoc />
    public partial class GeoReadsMatchArena : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No schema change: Geo's read-only view of matches (excluded from Geo's migrations)
            // now includes arena_id, created by Matches' AddMatchArena. Keeps the model in sync.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
