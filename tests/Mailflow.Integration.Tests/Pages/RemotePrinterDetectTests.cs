using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using PrinterEdit = Mailflow.Web.Pages.Printers.EditModel;

namespace Mailflow.Integration.Tests.Pages;

public sealed class RemotePrinterDetectTests : IDisposable
{
    private readonly SqliteFixture _fx = new();
    public void Dispose() => _fx.Dispose();

    private static (PrinterEdit page, IPrinterService local) Page(TestDb db)
    {
        var local = Substitute.For<IPrinterService>();
        local.GetInstalledPrinters().Returns(["Local printer"]);
        return (new PrinterEdit(db, local).With(), local);
    }

    private int AddAgent(string? printers)
    {
        using var db = _fx.NewDb();
        var a = new PrintAgent { Name = "Boutique", TokenHash = "h", CreatedAt = DateTime.UtcNow, PrinterNames = printers, PrintersReportedAt = printers is null ? null : DateTime.UtcNow };
        db.PrintAgents.Add(a);
        db.SaveChanges();
        return a.Id;
    }

    [Fact]
    public async Task A_remote_printer_offers_the_printers_the_agent_reported()
    {
        var id = AddAgent("HP LaserJet\nBrother");
        using var db = _fx.NewDb();
        var (page, local) = Page(db);
        page.Input = new PrinterEdit.PrinterInput { Kind = PrinterKind.PrintNode, PrintAgentId = id };

        var result = Assert.IsType<PartialViewResult>(await page.OnPostDetectAsync());

        Assert.Equal(["HP LaserJet", "Brother"], Assert.IsAssignableFrom<IReadOnlyList<string>>(result.Model));
        local.DidNotReceive().GetInstalledPrinters();
    }

    [Fact]
    public async Task A_local_printer_still_lists_the_printers_of_this_pc()
    {
        using var db = _fx.NewDb();
        var (page, _) = Page(db);
        page.Input = new PrinterEdit.PrinterInput { Kind = PrinterKind.Local };

        var result = Assert.IsType<PartialViewResult>(await page.OnPostDetectAsync());

        Assert.Equal(["Local printer"], Assert.IsAssignableFrom<IReadOnlyList<string>>(result.Model));
    }

    [Fact]
    public async Task Without_an_agent_or_a_report_the_user_is_told_what_to_do()
    {
        var waiting = AddAgent(null);
        using var db = _fx.NewDb();
        var (page, _) = Page(db);

        page.Input = new PrinterEdit.PrinterInput { Kind = PrinterKind.PrintNode };
        Assert.IsType<ContentResult>(await page.OnPostDetectAsync());

        page.Input = new PrinterEdit.PrinterInput { Kind = PrinterKind.PrintNode, PrintAgentId = waiting };
        Assert.IsType<ContentResult>(await page.OnPostDetectAsync());
    }
}
