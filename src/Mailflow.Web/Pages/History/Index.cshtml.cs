using Mailflow.Application.Abstractions;
using Mailflow.Application.Commands;
using Mailflow.Application.Queries;
using Mailflow.Domain.Entities;
using Mailflow.Infrastructure.Storage;
using Mailflow.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Mailflow.Web.Pages.History;

public sealed record HistoryRowModel(DocumentRow Row, IReadOnlyList<Printer> Printers, IReadOnlyList<Category> Categories, string? Message = null, bool Ok = true);

public class IndexModel(
    IAppDbContext db,
    DocumentSearch search,
    Reprint reprint,
    Reclassify reclassify,
    IOptions<DocumentStoreOptions> storeOptions) : PageModel
{
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true)] public string? Supplier { get; set; }
    [BindProperty(SupportsGet = true)] public int? CategoryId { get; set; }
    [BindProperty(SupportsGet = true)] public bool ErrorsOnly { get; set; }
    [BindProperty(SupportsGet = true)] public bool UnverifiedOnly { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;

    public PagedResult<DocumentRow> Result { get; private set; } = new([], 0, 1, DocumentSearch.PageSize);
    public List<Category> Categories { get; private set; } = [];
    public List<Printer> Printers { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (From is { } f && To is { } t && f > t) ModelState.AddModelError(nameof(To), Loc.T("La date de fin doit suivre la date de début."));
        Supplier = Supplier?.Trim();
        if (Supplier is { Length: > 200 }) Supplier = Supplier[..200];
        await LoadLookups(ct);
        if (!ModelState.IsValid) return;
        Result = await search.SearchAsync(new DocumentSearchFilter(From, To, Supplier, CategoryId, ErrorsOnly, Math.Max(1, PageNumber), UnverifiedOnly), ct);
    }

    public async Task<IActionResult> OnPostReprintAsync(int id, int? printerId, CancellationToken ct)
    {
        var r = await reprint.ExecuteAsync(id, printerId, ct);
        return await RowResult(id, r.Success ? Loc.T("Document envoyé à l'imprimante.") : Loc.T("Échec de l'impression : {0}", Loc.T(r.Error ?? "")), r.Success, ct);
    }

    public async Task<IActionResult> OnPostReclassifyAsync(int id, int categoryId, CancellationToken ct)
    {
        var r = await reclassify.ExecuteAsync(id, categoryId, ct);
        return await RowResult(id, r.Success ? Loc.T("Catégorie modifiée.") : (r.Error is null ? null : Loc.T(r.Error)), r.Success, ct);
    }

    public async Task<IActionResult> OnGetPdfAsync(int id, CancellationToken ct)
    {
        // The path comes from the database only, never from the request.
        var stored = await db.Documents.Where(d => d.Id == id).Select(d => d.StoredPath).FirstOrDefaultAsync(ct);
        var saved = await db.Settings.AsNoTracking().Where(s => s.Key == "DocumentsRoot").Select(s => s.Value).FirstOrDefaultAsync(ct);
        var root = string.IsNullOrWhiteSpace(saved) ? storeOptions.Value.DocumentsRoot : saved;
        if (string.IsNullOrWhiteSpace(stored) || string.IsNullOrWhiteSpace(root)) return NotFound();

        var fullRoot = Path.GetFullPath(root);
        var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(stored);
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(full)) return NotFound();

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Disposition"] = "inline";
        return PhysicalFile(full, "application/pdf");
    }

    private async Task<IActionResult> RowResult(int id, string? message, bool ok, CancellationToken ct)
    {
        var row = await search.GetRowAsync(id, ct);
        if (row is null) return NotFound();
        await LoadLookups(ct);
        return Partial("_Row", new HistoryRowModel(row, Printers, Categories, message, ok));
    }

    private async Task LoadLookups(CancellationToken ct)
    {
        Categories = await db.Categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
        Printers = await db.Printers.Where(p => p.IsActive).OrderBy(p => p.DisplayName).ToListAsync(ct);
    }
}
