using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quadra.Modules.Gamification.Migrations
{
    /// <inheritdoc />
    public partial class SeasonPassPurchases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "season_pass_purchases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    season_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    product_id = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    purchase_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    order_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    purchased_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_season_pass_purchases", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_season_pass_purchases_purchase_token_hash",
                table: "season_pass_purchases",
                column: "purchase_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_season_pass_purchases_user_id_season_code",
                table: "season_pass_purchases",
                columns: new[] { "user_id", "season_code" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "season_pass_purchases");
        }
    }
}
