using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quadra.Modules.Profile.Migrations
{
    /// <inheritdoc />
    public partial class AddCardStyle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "card_finish",
                table: "player_profiles",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Prisma");

            migrationBuilder.AddColumn<string>(
                name: "card_photo_object_key",
                table: "player_profiles",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "card_title",
                table: "player_profiles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "card_finish",
                table: "player_profiles");

            migrationBuilder.DropColumn(
                name: "card_photo_object_key",
                table: "player_profiles");

            migrationBuilder.DropColumn(
                name: "card_title",
                table: "player_profiles");
        }
    }
}
