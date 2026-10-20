using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quadra.Modules.Profile.Migrations
{
    /// <inheritdoc />
    public partial class AddPosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "posts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    photo_object_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    match_id = table.Column<Guid>(type: "uuid", nullable: true),
                    match_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_posts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_posts_author_id_created_at",
                table: "posts",
                columns: new[] { "author_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_posts_created_at",
                table: "posts",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_posts_match_id",
                table: "posts",
                column: "match_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "posts");
        }
    }
}
