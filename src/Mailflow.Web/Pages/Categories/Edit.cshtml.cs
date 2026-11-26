using System.ComponentModel.DataAnnotations;
using Mailflow.Application.Abstractions;
using Mailflow.Application.Processing;
using Mailflow.Domain.Entities;
using Mailflow.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Mailflow.Web.Pages.Categories;

public class EditModel(IAppDbContext db) : PageModel
{
    public class CategoryInput
    {
        [Required(ErrorMessage = "Le nom est requis."), StringLength(100)]
        public string Name { get; set; } = "";
        public string LocalSubfolder { get; set; } = "";
        [StringLength(200)]
        public string? ImapFolder { get; set; }
        [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Couleur invalide (#RRGGBB).")]
        public string Color { get; set; } = "#6c757d";
        public int SortOrder { get; set; }
    }

    [BindProperty] public CategoryInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is null) return Page();
        var c = await db.Categories.FindAsync(id.Value);
        if (c is null) return NotFound();
        Input = new CategoryInput { Name = c.Name, LocalSubfolder = c.LocalSubfolder, ImapFolder = c.ImapFolder, Color = c.Color, SortOrder = c.SortOrder };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Input.Name = (Input.Name ?? "").Trim();
        Input.LocalSubfolder = (Input.LocalSubfolder ?? "").Trim();
        if (Input.Name.Length == 0) ModelState.AddModelError("Input.Name", Loc.T("Le nom est requis."));
        if (!CategoryPath.IsValid(Input.LocalSubfolder))
            ModelState.AddModelError("Input.LocalSubfolder", Loc.T("Sous-dossier invalide : chemin relatif sans « .. », sans lecteur ni caractères interdits."));
        if (!System.Text.RegularExpressions.Regex.IsMatch(Input.Color ?? "", "^#[0-9a-fA-F]{6}$"))
            ModelState.AddModelError("Input.Color", Loc.T("Couleur invalide (#RRGGBB)."));
        Category? c = null;
        if (id is not null)
        {
            c = await db.Categories.FindAsync(id.Value);
            if (c is null) return NotFound();
            if (c.Name == MessagePipeline.DefaultCategoryName && Input.Name != c.Name)
                ModelState.AddModelError("Input.Name", Loc.T("La catégorie « A trier » ne peut pas être renommée : elle reçoit les e-mails sans règle."));
        }
        if (!ModelState.IsValid) return Page();

        if (c is null) db.Categories.Add(c = new Category());
        c.Name = Input.Name;
        c.LocalSubfolder = Input.LocalSubfolder;
        c.ImapFolder = string.IsNullOrWhiteSpace(Input.ImapFolder) ? null : Input.ImapFolder.Trim();
        c.Color = Input.Color!;
        c.SortOrder = Input.SortOrder;
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }
}
