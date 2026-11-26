using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mailflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgentPrinterList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PrinterNames",
                table: "PrintAgents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PrintersReportedAt",
                table: "PrintAgents",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrinterNames",
                table: "PrintAgents");

            migrationBuilder.DropColumn(
                name: "PrintersReportedAt",
                table: "PrintAgents");
        }
    }
}
