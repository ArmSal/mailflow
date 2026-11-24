using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Web.Pages.Mailboxes;

public class IndexModel(IAppDbContext db) : PageModel
{
    public List<Mailbox> Items { get; private set; } = [];
    public Dictionary<int, string> StoreNames { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Items = await db.Mailboxes.OrderBy(m => m.DisplayName).ToListAsync();
        StoreNames = await db.Stores.ToDictionaryAsync(s => s.Id, s => s.Name);
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var m = await db.Mailboxes.FindAsync(id);
        if (m is null) return NotFound();
        m.IsActive = !m.IsActive;
        await db.SaveChangesAsync();
        ViewData["StoreName"] = await db.Stores.Where(s => s.Id == m.StoreId).Select(s => s.Name).FirstOrDefaultAsync();
        return Partial("_MailboxRow", m);
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var m = await db.Mailboxes.FindAsync(id);
        if (m is null) return Content("");
        if (await db.ProcessedMessages.AnyAsync(p => p.MailboxId == id))
        {
            ViewData["Error"] = "Suppression impossible : des messages ont été traités. Désactivez la boîte.";
            ViewData["StoreName"] = await db.Stores.Where(s => s.Id == m.StoreId).Select(s => s.Name).FirstOrDefaultAsync();
            return Partial("_MailboxRow", m);
        }
        db.Mailboxes.Remove(m);
        await db.SaveChangesAsync();
        return Content("");
    }
}
