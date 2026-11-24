using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Web.Pages.Categories;

public class IndexModel(IAppDbContext db) : PageModel
{
    public List<Category> Items { get; private set; } = [];

    public async Task OnGetAsync() => Items = await db.Categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync();

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var c = await db.Categories.FindAsync(id);
        if (c is null) return Content("");
        if (await db.Documents.AnyAsync(d => d.CategoryId == id) || await db.RuleActions.AnyAsync(a => a.CategoryId == id))
        {
            ViewData["Error"] = "Suppression impossible : la catégorie est utilisée par des documents ou des règles.";
            return Partial("_CategoryRow", c);
        }
        db.Categories.Remove(c);
        await db.SaveChangesAsync();
        return Content("");
    }
}
