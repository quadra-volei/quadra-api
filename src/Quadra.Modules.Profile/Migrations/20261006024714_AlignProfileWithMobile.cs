using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quadra.Modules.Profile.Migrations
{
    /// <inheritdoc />
    public partial class AlignProfileWithMobile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "birth_date",
                table: "player_profiles",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "declared_level",
                table: "player_profiles",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "first_name",
                table: "player_profiles",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "handle",
                table: "player_profiles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_name",
                table: "player_profiles",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "onboarding_completed_at",
                table: "player_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "position",
                table: "player_profiles",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "preferred_modality",
                table: "player_profiles",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "position",
                table: "player_cards",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            // Carry existing data over to the new shape before the old columns go away:
            // the single display name becomes the first name (the "Player" placeholder becomes
            // empty), and the old position names become the app's three-letter codes.
            migrationBuilder.Sql(
                """
                UPDATE player_profiles
                SET first_name = CASE WHEN display_name = 'Player' THEN '' ELSE left(display_name, 40) END,
                    position = CASE primary_position
                        WHEN 'Setter' THEN 'LEV'
                        WHEN 'OutsideHitter' THEN 'PON'
                        WHEN 'Opposite' THEN 'OPO'
                        WHEN 'MiddleBlocker' THEN 'CEN'
                        WHEN 'Libero' THEN 'LIB'
                    END;

                UPDATE player_cards
                SET position = CASE primary_position
                        WHEN 'Setter' THEN 'LEV'
                        WHEN 'OutsideHitter' THEN 'PON'
                        WHEN 'Opposite' THEN 'OPO'
                        WHEN 'MiddleBlocker' THEN 'CEN'
                        WHEN 'Libero' THEN 'LIB'
                    END;
                """);

            migrationBuilder.DropColumn(
                name: "display_name",
                table: "player_profiles");

            migrationBuilder.DropColumn(
                name: "primary_position",
                table: "player_profiles");

            migrationBuilder.DropColumn(
                name: "secondary_position",
                table: "player_profiles");

            migrationBuilder.DropColumn(
                name: "primary_position",
                table: "player_cards");

            migrationBuilder.DropColumn(
                name: "secondary_position",
                table: "player_cards");

            migrationBuilder.CreateIndex(
                name: "uq_player_profiles_handle",
                table: "player_profiles",
                column: "handle",
                unique: true,
                filter: "handle IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_player_profiles_handle",
                table: "player_profiles");

            migrationBuilder.DropColumn(
                name: "birth_date",
                table: "player_profiles");

            migrationBuilder.DropColumn(
                name: "declared_level",
                table: "player_profiles");

            migrationBuilder.DropColumn(
                name: "first_name",
                table: "player_profiles");

            migrationBuilder.DropColumn(
                name: "handle",
                table: "player_profiles");

            migrationBuilder.DropColumn(
                name: "last_name",
                table: "player_profiles");

            migrationBuilder.DropColumn(
                name: "onboarding_completed_at",
                table: "player_profiles");

            migrationBuilder.DropColumn(
                name: "position",
                table: "player_profiles");

            migrationBuilder.DropColumn(
                name: "preferred_modality",
                table: "player_profiles");

            migrationBuilder.DropColumn(
                name: "position",
                table: "player_cards");

            migrationBuilder.AddColumn<string>(
                name: "display_name",
                table: "player_profiles",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "primary_position",
                table: "player_profiles",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "secondary_position",
                table: "player_profiles",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "primary_position",
                table: "player_cards",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "secondary_position",
                table: "player_cards",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);
        }
    }
}
