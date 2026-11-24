using System.Text.RegularExpressions;
using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Web.Pages.Rules;

public class AllowedSendersModel(IAppDbContext db) : PageModel
{
    // address (user@domain.tld) or domain pattern (domain.tld or @domain.tld)
    private static readonly Regex PatternRx = new(@"^(?:[^@\s]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}|@?[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,})$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    [BindProperty] public string Pattern { get; set; } = "";
    [BindProperty] public int? StoreId { get; set; }
    public List<AllowedSender> Items { get; private set; } = [];
    public List<Store> Stores { get; private set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAddAsync()
    {
        Pattern = (Pattern ?? "").Trim().ToLowerInvariant();
        if (!PatternRx.IsMatch(Pattern)) ModelState.AddModelError(nameof(Pattern), "Saisissez une adresse (ex. a@b.fr) ou un domaine (ex. b.fr).");
        else if (await db.AllowedSenders.AnyAsync(a => a.Pattern == Pattern && a.StoreId == StoreId)) ModelState.AddModelError(nameof(Pattern), "Ce motif existe déjà.");
        if (StoreId is int sid && !await db.Stores.AnyAsync(s => s.Id == sid)) ModelState.AddModelError(nameof(StoreId), "Magasin invalide.");
        if (ModelState.IsValid)
        {
            db.AllowedSenders.Add(new AllowedSender { Pattern = Pattern, StoreId = StoreId });
            await db.SaveChangesAsync();
            return RedirectToPage();
        }
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var a = await db.AllowedSenders.FindAsync(id);
        if (a is not null)
        {
            db.AllowedSenders.Remove(a);
            await db.SaveChangesAsync();
        }
        return Content("");
    }

    private async Task LoadAsync()
    {
        Items = await db.AllowedSenders.OrderBy(a => a.Pattern).ToListAsync();
        Stores = await db.Stores.OrderBy(s => s.Name).ToListAsync();
    }
}
