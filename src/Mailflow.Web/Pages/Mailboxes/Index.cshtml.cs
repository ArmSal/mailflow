using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Mailflow.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Web.Pages.Mailboxes;

public class IndexModel(IAppDbContext db) : PageModel
{
    public List<Mailbox> Items { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Items = await db.Mailboxes.OrderBy(m => m.DisplayName).ToListAsync();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var m = await db.Mailboxes.FindAsync(id);
        if (m is null) return NotFound();
        m.IsActive = !m.IsActive;
        await db.SaveChangesAsync();
        return this.PartialWith("_MailboxRow", m);
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var m = await db.Mailboxes.FindAsync(id);
        if (m is null) return Content("");
        if (await db.ProcessedMessages.AnyAsync(p => p.MailboxId == id))
        {
                return this.PartialWith("_MailboxRow", m, ("Error", Loc.T("Suppression impossible : des messages ont été traités. Désactivez la boîte.")));
        }
        db.Mailboxes.Remove(m);
        await db.SaveChangesAsync();
        return Content("");
    }
}
