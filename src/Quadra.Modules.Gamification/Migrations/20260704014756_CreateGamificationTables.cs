using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quadra.Modules.Gamification.Migrations
{
    /// <inheritdoc />
    public partial class CreateGamificationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "group_rankings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    total_points = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    matches_counted = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_match_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_match_date_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_rankings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "point_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    match_date_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    awarded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_point_transactions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_group_rankings_group_points",
                table: "group_rankings",
                columns: new[] { "group_id", "total_points", "user_id" },
                descending: new[] { false, true, false });

            migrationBuilder.CreateIndex(
                name: "uq_group_rankings_group_user",
                table: "group_rankings",
                columns: new[] { "group_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_point_transactions_group_match",
                table: "point_transactions",
                columns: new[] { "group_id", "match_id" });

            migrationBuilder.CreateIndex(
                name: "ix_point_transactions_group_user_date",
                table: "point_transactions",
                columns: new[] { "group_id", "user_id", "match_date_time" });

            migrationBuilder.CreateIndex(
                name: "uq_point_transactions_user_match_reason",
                table: "point_transactions",
                columns: new[] { "user_id", "match_id", "reason" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "group_rankings");

            migrationBuilder.DropTable(
                name: "point_transactions");
        }
    }
}
