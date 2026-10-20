using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quadra.Modules.Auth.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceCognitoWithOwnIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sessions issued through Cognito cannot be refreshed by this API; drop them so every
            // client signs in again. Existing users rows are kept (other modules reference users.id);
            // a pre-existing Google/Apple row has no external_subject and is not matched on login.
            migrationBuilder.Sql("DELETE FROM refresh_tokens;");

            migrationBuilder.DropIndex(
                name: "ix_users_cognito_sub",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_refresh_tokens_cognito_sub",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "cognito_sub",
                table: "users");

            migrationBuilder.DropColumn(
                name: "confirmation_status",
                table: "users");

            migrationBuilder.DropColumn(
                name: "cognito_sub",
                table: "refresh_tokens");

            migrationBuilder.AddColumn<string>(
                name: "external_subject",
                table: "users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_provider_external_subject",
                table: "users",
                columns: new[] { "provider", "external_subject" },
                unique: true,
                filter: "external_subject IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_provider_external_subject",
                table: "users");

            migrationBuilder.DropColumn(
                name: "external_subject",
                table: "users");

            migrationBuilder.AddColumn<string>(
                name: "cognito_sub",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "confirmation_status",
                table: "users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "cognito_sub",
                table: "refresh_tokens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // The dropped values are gone; backfill something unique so the index can be rebuilt.
            migrationBuilder.Sql("UPDATE users SET cognito_sub = id::text, confirmation_status = 'Confirmed';");

            migrationBuilder.CreateIndex(
                name: "ix_users_cognito_sub",
                table: "users",
                column: "cognito_sub",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_cognito_sub",
                table: "refresh_tokens",
                column: "cognito_sub");
        }
    }
}
