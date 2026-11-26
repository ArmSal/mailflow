using System.ComponentModel.DataAnnotations;
using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;
using Mailflow.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Web.Pages.Printers;

public class EditModel(IAppDbContext db, IPrinterService printers) : PageModel
{
    public class PrinterInput
    {
        [Required(ErrorMessage = "Le nom est requis."), StringLength(100)]
        public string DisplayName { get; set; } = "";
        [Required(ErrorMessage = "L'imprimante Windows est requise."), StringLength(260)]
        public string WindowsName { get; set; } = "";
        public PrinterKind Kind { get; set; } = PrinterKind.Local;
        public int? PrintAgentId { get; set; }
        [Range(1, 99, ErrorMessage = "Copies : 1 à 99.")]
        public int DefaultCopies { get; set; } = 1;
        public bool Duplex { get; set; }
        public bool IsActive { get; set; } = true;
    }

    [BindProperty] public PrinterInput Input { get; set; } = new();
    public List<PrintAgent> Agents { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        Agents = await db.PrintAgents.OrderBy(a => a.Name).ToListAsync();
        if (id is null) return Page();
        var p = await db.Printers.FindAsync(id.Value);
        if (p is null) return NotFound();
        Input = new PrinterInput { DisplayName = p.DisplayName, WindowsName = p.WindowsName, Kind = p.Kind, PrintAgentId = p.PrintAgentId, DefaultCopies = p.DefaultCopies, Duplex = p.Duplex, IsActive = p.IsActive };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Input.DisplayName = (Input.DisplayName ?? "").Trim();
        Input.WindowsName = (Input.WindowsName ?? "").Trim();
        if (Input.DisplayName.Length == 0) ModelState.AddModelError("Input.DisplayName", Loc.T("Le nom est requis."));
        if (Input.WindowsName.Length == 0) ModelState.AddModelError("Input.WindowsName", Loc.T("L'imprimante Windows est requise."));
        if (Input.DefaultCopies is < 1 or > 99) ModelState.AddModelError("Input.DefaultCopies", Loc.T("Copies : 1 à 99."));
        if (!Enum.IsDefined(Input.Kind)) ModelState.AddModelError("Input.Kind", Loc.T("Type invalide."));
        if (Input.Kind == PrinterKind.PrintNode && (Input.PrintAgentId is null || !await db.PrintAgents.AnyAsync(a => a.Id == Input.PrintAgentId)))
            ModelState.AddModelError("Input.PrintAgentId", Loc.T("Choisissez un agent d'impression pour une imprimante PrintNode."));
        if (!ModelState.IsValid)
        {
            Agents = await db.PrintAgents.OrderBy(a => a.Name).ToListAsync();
            return Page();
        }

        Printer? p = null;
        if (id is not null)
        {
            p = await db.Printers.FindAsync(id.Value);
            if (p is null) return NotFound();
        }
        else db.Printers.Add(p = new Printer());
        p.DisplayName = Input.DisplayName;
        p.WindowsName = Input.WindowsName;
        p.Kind = Input.Kind;
        p.PrintAgentId = Input.Kind == PrinterKind.PrintNode ? Input.PrintAgentId : null;
        p.DefaultCopies = Input.DefaultCopies;
        p.Duplex = Input.Duplex;
        p.IsActive = Input.IsActive;
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDetectAsync()
    {
        // A remote printer is installed on the agent's PC: offer what that agent reported, not this PC's printers.
        if (Input.Kind == PrinterKind.PrintNode)
        {
            if (Input.PrintAgentId is null) return Content(System.Net.WebUtility.HtmlEncode(Loc.T("Choisissez d'abord un agent d'impression.")), "text/html");
            var agent = await db.PrintAgents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == Input.PrintAgentId);
            if (agent?.PrintersReportedAt is null)
                return Content(System.Net.WebUtility.HtmlEncode(Loc.T("L'agent n'a pas encore envoyé la liste de ses imprimantes : démarrez-le, puis réessayez dans un instant.")), "text/html");
            return Partial("_Detected", Mailflow.Application.Agents.AgentJobService.PrinterList(agent));
        }
        try { return Partial("_Detected", printers.GetInstalledPrinters()); }
        catch (Exception ex) { return Content(Loc.T("Détection impossible :") + " " + System.Net.WebUtility.HtmlEncode(ex.Message), "text/html"); }
    }

}
