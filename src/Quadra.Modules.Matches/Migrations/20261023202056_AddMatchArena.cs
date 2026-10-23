using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quadra.Modules.Matches.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchArena : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "arena_id",
                table: "matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_matches_arena_id",
                table: "matches",
                column: "arena_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_matches_arena_id",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "arena_id",
                table: "matches");
        }
    }
}
