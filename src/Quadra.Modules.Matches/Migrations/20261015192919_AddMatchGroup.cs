using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quadra.Modules.Matches.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "group_id",
                table: "matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_matches_group_id",
                table: "matches",
                column: "group_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_matches_group_id",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "group_id",
                table: "matches");
        }
    }
}
