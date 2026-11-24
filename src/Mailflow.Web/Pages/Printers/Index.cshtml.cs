using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Web.Pages.Printers;

public class IndexModel(IAppDbContext db, IPrinterService printers, ITestPageProvider testPage) : PageModel
{
    public List<Printer> Items { get; private set; } = [];
    public Dictionary<int, string> StoreNames { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Items = await db.Printers.OrderBy(p => p.DisplayName).ToListAsync();
        StoreNames = await db.Stores.ToDictionaryAsync(s => s.Id, s => s.Name);
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var p = await db.Printers.FindAsync(id);
        if (p is null) return NotFound();
        p.IsActive = !p.IsActive;
        await db.SaveChangesAsync();
        return this.PartialWith("_PrinterRow", p, ("StoreName", await StoreNameAsync(p)));
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var p = await db.Printers.FindAsync(id);
        if (p is null) return Content("");
        if (await db.Documents.AnyAsync(d => d.PrinterId == id) || await db.RuleActions.AnyAsync(a => a.PrinterId == id))
        {
            return this.PartialWith("_PrinterRow", p,
                ("Error", "Suppression impossible : l'imprimante est utilisée. Désactivez-la."), ("StoreName", await StoreNameAsync(p)));
        }
        db.Printers.Remove(p);
        await db.SaveChangesAsync();
        return Content("");
    }

    public async Task<IActionResult> OnPostTestPrintAsync(int id, CancellationToken ct)
    {
        var p = await db.Printers.FindAsync(id);
        if (p is null) return NotFound();
        bool ok;
        string message;
        try
        {
            await printers.PrintAsync(testPage.GetTestPagePath(), p.WindowsName, 1, p.Duplex, ct);
            ok = true;
            message = "Page de test envoyée.";
        }
        catch (Exception ex)
        {
            ok = false;
            message = "Échec de l'impression : " + ex.Message;
        }
        return this.PartialWith("_TestPrintResult", null, ("Ok", ok), ("Message", message));
    }

    private Task<string?> StoreNameAsync(Printer p) =>
        db.Stores.Where(s => s.Id == p.StoreId).Select(s => s.Name).FirstOrDefaultAsync();
}
