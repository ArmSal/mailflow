using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Web.Pages.Rules;

public class IndexModel(IAppDbContext db) : PageModel
{
    public RuleListVm List { get; private set; } = new([], new Dictionary<int, string>());

    public async Task OnGetAsync() => List = await LoadAsync();

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var r = await db.Rules.FindAsync(id);
        if (r is null) return NotFound();
        r.IsActive = !r.IsActive;
        await db.SaveChangesAsync();
        return Partial("_RuleList", await LoadAsync());
    }

    /// <summary>Moves a rule up or down; priorities are renumbered 1..n first.</summary>
    public async Task<IActionResult> OnPostMoveAsync(int id, string dir)
    {
        var rules = await db.Rules.OrderBy(r => r.Priority).ThenBy(r => r.Id).ToListAsync();
        for (var i = 0; i < rules.Count; i++) rules[i].Priority = i + 1;
        var idx = rules.FindIndex(r => r.Id == id);
        var other = dir == "up" ? idx - 1 : dir == "down" ? idx + 1 : -1;
        if (idx >= 0 && other >= 0 && other < rules.Count)
            (rules[idx].Priority, rules[other].Priority) = (rules[other].Priority, rules[idx].Priority);
        await db.SaveChangesAsync();
        return Partial("_RuleList", await LoadAsync());
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var r = await db.Rules.Include(x => x.Conditions).Include(x => x.Actions).FirstOrDefaultAsync(x => x.Id == id);
        if (r is not null)
        {
            db.RuleConditions.RemoveRange(r.Conditions);
            db.RuleActions.RemoveRange(r.Actions);
            db.Rules.Remove(r);
            await db.SaveChangesAsync();
        }
        return Partial("_RuleList", await LoadAsync());
    }

    private async Task<RuleListVm> LoadAsync() => new(
        await db.Rules.Include(r => r.Conditions).Include(r => r.Actions).OrderBy(r => r.Priority).ThenBy(r => r.Id).ToListAsync(),
        await db.Mailboxes.ToDictionaryAsync(m => m.Id, m => m.DisplayName));
}
