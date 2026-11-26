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
        // Print jobs reference documents without a cascade: detach them first or the delete fails on the foreign key.
        var docIds = db.Documents.Where(d => db.ProcessedMessages.Any(p => p.Id == d.ProcessedMessageId && p.MailboxId == id)).Select(d => d.Id);
        await db.PrintJobs.Where(j => j.DocumentId != null && docIds.Contains(j.DocumentId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.DocumentId, (int?)null));
        db.Mailboxes.Remove(m);
        await db.SaveChangesAsync();
        return Content("");
    }
}
