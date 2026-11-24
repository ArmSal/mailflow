using System.ComponentModel.DataAnnotations;
using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Mailflow.Web.Pages.Stores;

public class EditModel(IAppDbContext db) : PageModel
{
    public class StoreInput
    {
        [Required(ErrorMessage = "Le nom est requis."), StringLength(100)]
        public string Name { get; set; } = "";
        [StringLength(300)]
        public string? Address { get; set; }
        public bool IsActive { get; set; } = true;
    }

    [BindProperty] public StoreInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is null) return Page();
        var s = await db.Stores.FindAsync(id.Value);
        if (s is null) return NotFound();
        Input = new StoreInput { Name = s.Name, Address = s.Address, IsActive = s.IsActive };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Input.Name = (Input.Name ?? "").Trim();
        if (Input.Name.Length == 0) ModelState.AddModelError("Input.Name", "Le nom est requis.");
        if (!ModelState.IsValid) return Page();

        Store? s = null;
        if (id is not null)
        {
            s = await db.Stores.FindAsync(id.Value);
            if (s is null) return NotFound();
        }
        else db.Stores.Add(s = new Store());
        s.Name = Input.Name;
        s.Address = string.IsNullOrWhiteSpace(Input.Address) ? null : Input.Address.Trim();
        s.IsActive = Input.IsActive;
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }
}
