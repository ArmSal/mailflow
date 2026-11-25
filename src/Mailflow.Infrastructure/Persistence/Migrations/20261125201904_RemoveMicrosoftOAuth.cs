using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mailflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveMicrosoftOAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Microsoft sign-in is gone: switch those mailboxes off (they have no password) and say why.
            migrationBuilder.Sql("UPDATE Mailboxes SET IsActive = 0, LastError = 'Connexion Microsoft supprimée : modifiez la boîte et saisissez un mot de passe.' WHERE AuthMethod = 'MicrosoftOAuth';");
            migrationBuilder.Sql("DELETE FROM Settings WHERE Key = 'OAuthMicrosoftClientId';");

            migrationBuilder.DropColumn(
                name: "AuthMethod",
                table: "Mailboxes");

            migrationBuilder.DropColumn(
                name: "EncryptedOAuthCache",
                table: "Mailboxes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthMethod",
                table: "Mailboxes",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EncryptedOAuthCache",
                table: "Mailboxes",
                type: "TEXT",
                nullable: true);
        }
    }
}
