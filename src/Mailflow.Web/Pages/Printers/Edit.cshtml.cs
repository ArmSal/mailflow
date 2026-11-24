using System.ComponentModel.DataAnnotations;
using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Web.Pages.Printers;

public class EditModel(IAppDbContext db, IPrinterService printers) : PageModel
{
    public class PrinterInput
    {
        public int StoreId { get; set; }
        [Required(ErrorMessage = "Le nom est requis."), StringLength(100)]
        public string DisplayName { get; set; } = "";
        [Required(ErrorMessage = "L'imprimante Windows est requise."), StringLength(260)]
        public string WindowsName { get; set; } = "";
        public PrinterKind Kind { get; set; } = PrinterKind.Local;
        [Range(1, 99, ErrorMessage = "Copies : 1 à 99.")]
        public int DefaultCopies { get; set; } = 1;
        public bool Duplex { get; set; }
        public bool IsActive { get; set; } = true;
    }

    [BindProperty] public PrinterInput Input { get; set; } = new();
    public List<Store> Stores { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        await LoadStores();
        if (id is null) return Page();
        var p = await db.Printers.FindAsync(id.Value);
        if (p is null) return NotFound();
        Input = new PrinterInput { StoreId = p.StoreId, DisplayName = p.DisplayName, WindowsName = p.WindowsName, Kind = p.Kind, DefaultCopies = p.DefaultCopies, Duplex = p.Duplex, IsActive = p.IsActive };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Input.DisplayName = (Input.DisplayName ?? "").Trim();
        Input.WindowsName = (Input.WindowsName ?? "").Trim();
        if (Input.DisplayName.Length == 0) ModelState.AddModelError("Input.DisplayName", "Le nom est requis.");
        if (Input.WindowsName.Length == 0) ModelState.AddModelError("Input.WindowsName", "L'imprimante Windows est requise.");
        if (Input.DefaultCopies is < 1 or > 99) ModelState.AddModelError("Input.DefaultCopies", "Copies : 1 à 99.");
        if (!Enum.IsDefined(Input.Kind)) ModelState.AddModelError("Input.Kind", "Type invalide.");
        if (!await db.Stores.AnyAsync(s => s.Id == Input.StoreId)) ModelState.AddModelError("Input.StoreId", "Magasin invalide.");
        if (!ModelState.IsValid) { await LoadStores(); return Page(); }

        Printer? p = null;
        if (id is not null)
        {
            p = await db.Printers.FindAsync(id.Value);
            if (p is null) return NotFound();
        }
        else db.Printers.Add(p = new Printer());
        p.StoreId = Input.StoreId;
        p.DisplayName = Input.DisplayName;
        p.WindowsName = Input.WindowsName;
        p.Kind = Input.Kind;
        p.DefaultCopies = Input.DefaultCopies;
        p.Duplex = Input.Duplex;
        p.IsActive = Input.IsActive;
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    public IActionResult OnPostDetect()
    {
        try { return Partial("_Detected", printers.GetInstalledPrinters()); }
        catch (Exception ex) { return Content("Détection impossible : " + System.Net.WebUtility.HtmlEncode(ex.Message), "text/html"); }
    }

    private async Task LoadStores() => Stores = await db.Stores.OrderBy(s => s.Name).ToListAsync();
}
