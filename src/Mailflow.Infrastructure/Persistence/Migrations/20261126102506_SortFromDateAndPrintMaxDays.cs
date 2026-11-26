using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mailflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SortFromDateAndPrintMaxDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SortFromDate",
                table: "Mailboxes",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "PrintMaxDays",
                table: "Mailboxes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            // Keep each mailbox's current behaviour: the rolling lookback becomes a start date, and the global
            // print age (hours) becomes whole days clamped to 1-7.
            migrationBuilder.Sql("UPDATE Mailboxes SET SortFromDate = strftime('%Y-%m-%d 00:00:00', 'now', '-' || LookbackDays || ' days')");
            migrationBuilder.Sql("UPDATE Mailboxes SET PrintMaxDays = COALESCE((SELECT MIN(7, MAX(1, (CAST(Value AS INTEGER) + 23) / 24)) FROM Settings WHERE Key = 'PrintMaxAgeHours'), 1)");
            migrationBuilder.Sql("DELETE FROM Settings WHERE Key = 'PrintMaxAgeHours'");

            migrationBuilder.DropColumn(
                name: "LookbackDays",
                table: "Mailboxes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LookbackDays",
                table: "Mailboxes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.DropColumn(
                name: "SortFromDate",
                table: "Mailboxes");

            migrationBuilder.DropColumn(
                name: "PrintMaxDays",
                table: "Mailboxes");
        }
    }
}
