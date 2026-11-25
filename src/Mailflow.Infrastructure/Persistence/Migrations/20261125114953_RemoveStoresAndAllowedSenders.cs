using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mailflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveStoresAndAllowedSenders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQLite rebuilds Mailboxes/Printers to drop a column; keep rows that other tables reference.
            migrationBuilder.Sql("PRAGMA foreign_keys = OFF;", suppressTransaction: true);

            migrationBuilder.DropForeignKey(
                name: "FK_Mailboxes_Stores_StoreId",
                table: "Mailboxes");

            migrationBuilder.DropForeignKey(
                name: "FK_Printers_Stores_StoreId",
                table: "Printers");

            migrationBuilder.DropTable(
                name: "AllowedSenders");

            migrationBuilder.DropTable(
                name: "Stores");

            migrationBuilder.DropIndex(
                name: "IX_Printers_StoreId",
                table: "Printers");

            migrationBuilder.DropIndex(
                name: "IX_Mailboxes_StoreId",
                table: "Mailboxes");

            migrationBuilder.DropColumn(
                name: "StoreId",
                table: "Printers");

            migrationBuilder.DropColumn(
                name: "StoreId",
                table: "Mailboxes");

            migrationBuilder.Sql("PRAGMA foreign_keys = ON;", suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StoreId",
                table: "Printers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StoreId",
                table: "Mailboxes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Stores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Address = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AllowedSenders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Pattern = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllowedSenders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllowedSenders_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Printers_StoreId",
                table: "Printers",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Mailboxes_StoreId",
                table: "Mailboxes",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_AllowedSenders_StoreId",
                table: "AllowedSenders",
                column: "StoreId");

            migrationBuilder.AddForeignKey(
                name: "FK_Mailboxes_Stores_StoreId",
                table: "Mailboxes",
                column: "StoreId",
                principalTable: "Stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Printers_Stores_StoreId",
                table: "Printers",
                column: "StoreId",
                principalTable: "Stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
