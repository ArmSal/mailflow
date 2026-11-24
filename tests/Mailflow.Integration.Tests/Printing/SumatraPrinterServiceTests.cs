using Mailflow.Infrastructure.Printing;
using Microsoft.Extensions.Options;

namespace Mailflow.Integration.Tests.Printing;

public class SumatraPrinterServiceTests
{
    private sealed class FakeCatalog(params string[] printers) : IPrinterCatalog
    {
        public IReadOnlyList<string> GetInstalledPrinters() => printers;
    }

    private static SumatraPrinterService Service(params string[] printers) =>
        new(new FakeCatalog(printers), Options.Create(new SumatraOptions { ExePath = "missing.exe" }));

    [Fact]
    public void Arguments_are_separate_items_with_settings()
    {
        var a = SumatraPrinterService.BuildArguments(@"C:\a b\x.pdf", "HP \"Laser\" & calc", 2, true);
        Assert.Equal(["-print-to", "HP \"Laser\" & calc", "-silent", "-print-settings", "2x,duplexlong", @"C:\a b\x.pdf"], a);
        Assert.Contains("1x,simplex", SumatraPrinterService.BuildArguments("p", "n", 1, false));
    }

    [Fact]
    public async Task Missing_pdf_throws()
    {
        var ex = await Assert.ThrowsAsync<PrintingException>(() =>
            Service("P").PrintAsync(@"C:\nope.pdf", "P", 1, false, CancellationToken.None));
        Assert.Contains("PDF introuvable", ex.Message);
    }

    [Fact]
    public async Task Unknown_printer_throws()
    {
        var pdf = Path.GetTempFileName();
        try
        {
            var ex = await Assert.ThrowsAsync<PrintingException>(() =>
                Service("P").PrintAsync(pdf, "Other", 1, false, CancellationToken.None));
            Assert.Contains("non installee", ex.Message);
        }
        finally { File.Delete(pdf); }
    }
}
