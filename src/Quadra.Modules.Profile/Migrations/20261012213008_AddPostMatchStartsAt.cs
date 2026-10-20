using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quadra.Modules.Profile.Migrations
{
    /// <inheritdoc />
    public partial class AddPostMatchStartsAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "match_starts_at",
                table: "posts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "match_starts_at",
                table: "posts");
        }
    }
}
