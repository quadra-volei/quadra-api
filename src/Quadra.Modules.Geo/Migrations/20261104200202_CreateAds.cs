using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Quadra.Modules.Geo.Migrations
{
    /// <inheritdoc />
    public partial class CreateAds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    advertiser_id = table.Column<Guid>(type: "uuid", nullable: true),
                    placement = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    title = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    body = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    photo_object_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    link_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    link_value = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    location = table.Column<Point>(type: "geography (point, 4326)", nullable: false),
                    radius_km = table.Column<int>(type: "integer", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    weeks = table.Column<int>(type: "integer", nullable: false),
                    price_cents = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    payment_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ads", x => x.id);
                    table.CheckConstraint("ck_ads_ends_on", "ends_on = starts_on + 7 * weeks");
                    table.CheckConstraint("ck_ads_link_kind", "link_kind IN ('None', 'Arena', 'WhatsApp', 'Url')");
                    table.CheckConstraint("ck_ads_paid", "status NOT IN ('InReview', 'Approved') OR paid_at IS NOT NULL");
                    table.CheckConstraint("ck_ads_placement", "placement IN ('Home', 'Map', 'Opening')");
                    table.CheckConstraint("ck_ads_price", "price_cents > 0");
                    table.CheckConstraint("ck_ads_radius", "radius_km BETWEEN 1 AND 50");
                    table.CheckConstraint("ck_ads_status", "status IN ('AwaitingPayment', 'InReview', 'Approved', 'Rejected', 'Cancelled')");
                    table.CheckConstraint("ck_ads_weeks", "weeks BETWEEN 1 AND 12");
                });

            migrationBuilder.CreateTable(
                name: "ad_views",
                columns: table => new
                {
                    ad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ad_views", x => new { x.ad_id, x.user_id, x.day, x.kind });
                    table.CheckConstraint("ck_ad_views_kind", "kind IN ('Impression', 'Click')");
                    table.ForeignKey(
                        name: "FK_ad_views_ads_ad_id",
                        column: x => x.ad_id,
                        principalTable: "ads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ad_views_user_id",
                table: "ad_views",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_ads_advertiser_id",
                table: "ads",
                column: "advertiser_id");

            migrationBuilder.CreateIndex(
                name: "ix_ads_location",
                table: "ads",
                column: "location")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_ads_status_placement_starts",
                table: "ads",
                columns: new[] { "status", "placement", "starts_on" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ad_views");

            migrationBuilder.DropTable(
                name: "ads");
        }
    }
}
