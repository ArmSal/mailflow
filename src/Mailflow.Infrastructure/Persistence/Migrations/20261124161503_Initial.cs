using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mailflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    LocalSubfolder = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ImapFolder = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Color = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Stores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "Mailboxes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    Host = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    UseSsl = table.Column<bool>(type: "INTEGER", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    EncryptedPassword = table.Column<string>(type: "TEXT", nullable: false),
                    InboxFolder = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    CheckIntervalMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    LookbackDays = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultAfterProcessing = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastCheckAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mailboxes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Mailboxes_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Printers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    WindowsName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    DefaultCopies = table.Column<int>(type: "INTEGER", nullable: false),
                    Duplex = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Printers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Printers_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Rules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    MatchMode = table.Column<string>(type: "TEXT", nullable: false),
                    StopProcessing = table.Column<bool>(type: "INTEGER", nullable: false),
                    MailboxId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Rules_Mailboxes_MailboxId",
                        column: x => x.MailboxId,
                        principalTable: "Mailboxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RunLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    MailboxId = table.Column<int>(type: "INTEGER", nullable: true),
                    MessagesHandled = table.Column<int>(type: "INTEGER", nullable: false),
                    DocumentsSaved = table.Column<int>(type: "INTEGER", nullable: false),
                    Printed = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunLogs_Mailboxes_MailboxId",
                        column: x => x.MailboxId,
                        principalTable: "Mailboxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MailboxId = table.Column<int>(type: "INTEGER", nullable: false),
                    MessageId = table.Column<string>(type: "TEXT", maxLength: 998, nullable: false),
                    ImapUid = table.Column<uint>(type: "INTEGER", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    From = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", nullable: false),
                    MatchedRuleId = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessedMessages_Mailboxes_MailboxId",
                        column: x => x.MailboxId,
                        principalTable: "Mailboxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProcessedMessages_Rules_MatchedRuleId",
                        column: x => x.MatchedRuleId,
                        principalTable: "Rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RuleActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RuleId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: true),
                    PrinterId = table.Column<int>(type: "INTEGER", nullable: true),
                    Copies = table.Column<int>(type: "INTEGER", nullable: true),
                    ImapFolder = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RuleActions_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RuleActions_Printers_PrinterId",
                        column: x => x.PrinterId,
                        principalTable: "Printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RuleActions_Rules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "Rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RuleConditions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RuleId = table.Column<int>(type: "INTEGER", nullable: false),
                    Field = table.Column<string>(type: "TEXT", nullable: false),
                    Operator = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    CaseSensitive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleConditions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RuleConditions_Rules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "Rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProcessedMessageId = table.Column<int>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OriginalName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    StoredPath = table.Column<string>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    PrintedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PrinterId = table.Column<int>(type: "INTEGER", nullable: true),
                    PrintError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documents_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Documents_Printers_PrinterId",
                        column: x => x.PrinterId,
                        principalTable: "Printers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Documents_ProcessedMessages_ProcessedMessageId",
                        column: x => x.ProcessedMessageId,
                        principalTable: "ProcessedMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AllowedSenders_StoreId",
                table: "AllowedSenders",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_CategoryId",
                table: "Documents",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_PrinterId",
                table: "Documents",
                column: "PrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_ProcessedMessageId",
                table: "Documents",
                column: "ProcessedMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_Sha256",
                table: "Documents",
                column: "Sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Mailboxes_StoreId",
                table: "Mailboxes",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Printers_StoreId",
                table: "Printers",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedMessages_MailboxId_MessageId",
                table: "ProcessedMessages",
                columns: new[] { "MailboxId", "MessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedMessages_MatchedRuleId",
                table: "ProcessedMessages",
                column: "MatchedRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_RuleActions_CategoryId",
                table: "RuleActions",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_RuleActions_PrinterId",
                table: "RuleActions",
                column: "PrinterId");

            migrationBuilder.CreateIndex(
                name: "IX_RuleActions_RuleId",
                table: "RuleActions",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_RuleConditions_RuleId",
                table: "RuleConditions",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Rules_MailboxId",
                table: "Rules",
                column: "MailboxId");

            migrationBuilder.CreateIndex(
                name: "IX_RunLogs_MailboxId",
                table: "RunLogs",
                column: "MailboxId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AllowedSenders");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "RuleActions");

            migrationBuilder.DropTable(
                name: "RuleConditions");

            migrationBuilder.DropTable(
                name: "RunLogs");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "ProcessedMessages");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "Printers");

            migrationBuilder.DropTable(
                name: "Rules");

            migrationBuilder.DropTable(
                name: "Mailboxes");

            migrationBuilder.DropTable(
                name: "Stores");
        }
    }
}
