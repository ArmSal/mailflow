using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Web.Pages.Stores;

public class IndexModel(IAppDbContext db) : PageModel
{
    public List<Store> Items { get; private set; } = [];

    public async Task OnGetAsync() => Items = await db.Stores.OrderBy(s => s.Name).ToListAsync();

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var s = await db.Stores.FindAsync(id);
        if (s is null) return NotFound();
        s.IsActive = !s.IsActive;
        await db.SaveChangesAsync();
        return Partial("_StoreRow", s);
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var s = await db.Stores.FindAsync(id);
        if (s is null) return Content("");
        if (await db.Mailboxes.AnyAsync(m => m.StoreId == id) || await db.Printers.AnyAsync(p => p.StoreId == id))
        {
            return this.PartialWith("_StoreRow", s, ("Error", "Suppression impossible : des boîtes mail ou imprimantes sont liées. Désactivez le magasin."));
        }
        db.Stores.Remove(s);
        await db.SaveChangesAsync();
        return Content("");
    }
}
