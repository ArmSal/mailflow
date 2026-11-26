using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mailflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DuplicateDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_Sha256",
                table: "Documents");

            migrationBuilder.AddColumn<int>(
                name: "DuplicateOfDocumentId",
                table: "Documents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_DuplicateOfDocumentId",
                table: "Documents",
                column: "DuplicateOfDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_Sha256",
                table: "Documents",
                column: "Sha256",
                unique: true,
                filter: "DuplicateOfDocumentId IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Documents_DuplicateOfDocumentId",
                table: "Documents",
                column: "DuplicateOfDocumentId",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Documents_DuplicateOfDocumentId",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_DuplicateOfDocumentId",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_Sha256",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "DuplicateOfDocumentId",
                table: "Documents");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_Sha256",
                table: "Documents",
                column: "Sha256",
                unique: true);
        }
    }
}
