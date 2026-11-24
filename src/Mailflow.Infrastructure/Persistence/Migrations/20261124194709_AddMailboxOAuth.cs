using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mailflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMailboxOAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthMethod",
                table: "Mailboxes",
                type: "TEXT",
                nullable: false,
                defaultValue: "Password"); // existing mailboxes keep signing in with their password

            migrationBuilder.AddColumn<string>(
                name: "EncryptedOAuthCache",
                table: "Mailboxes",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthMethod",
                table: "Mailboxes");

            migrationBuilder.DropColumn(
                name: "EncryptedOAuthCache",
                table: "Mailboxes");
        }
    }
}
