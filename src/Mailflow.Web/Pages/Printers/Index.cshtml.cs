using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Mailflow.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Web.Pages.Printers;

public class IndexModel(IAppDbContext db, IPrinterService printers, ITestPageProvider testPage) : PageModel
{
    public List<Printer> Items { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Items = await db.Printers.OrderBy(p => p.DisplayName).ToListAsync();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var p = await db.Printers.FindAsync(id);
        if (p is null) return NotFound();
        p.IsActive = !p.IsActive;
        await db.SaveChangesAsync();
        return this.PartialWith("_PrinterRow", p);
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var p = await db.Printers.FindAsync(id);
        if (p is null) return Content("");
        if (await db.Documents.AnyAsync(d => d.PrinterId == id) || await db.RuleActions.AnyAsync(a => a.PrinterId == id))
        {
            return this.PartialWith("_PrinterRow", p,
                ("Error", Loc.T("Suppression impossible : l'imprimante est utilisée. Désactivez-la.")));
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
            message = Loc.T("Page de test envoyée.");
        }
        catch (Exception ex)
        {
            ok = false;
            message = Loc.T("Échec de l'impression :") + " " + ex.Message;
        }
        return this.PartialWith("_TestPrintResult", null, ("Ok", ok), ("Message", message));
    }

}
