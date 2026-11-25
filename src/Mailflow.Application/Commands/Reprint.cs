using Mailflow.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Application.Commands;

public sealed record ReprintResult(bool Success, string? Error);

public sealed class Reprint(IAppDbContext db, IPrinterService printers, IClock clock)
{
    /// <summary>Never throws: failures are returned and stored in Document.PrintError.</summary>
    public async Task<ReprintResult> ExecuteAsync(int documentId, int? printerId, CancellationToken ct = default)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null) return new(false, "Document introuvable.");

        var wanted = printerId ?? doc.PrinterId;
        var printer = wanted is { } id
            ? await db.Printers.FirstOrDefaultAsync(p => p.Id == id && p.IsActive, ct)
            : null;
        if (printer is null && printerId is null)
            printer = await db.Printers.Where(p => p.IsActive).OrderBy(p => p.Id).FirstOrDefaultAsync(ct);
        if (printer is null) return new(false, "Aucune imprimante active disponible.");

        try
        {
            await printers.PrintAsync(doc.StoredPath, printer.WindowsName, printer.DefaultCopies, printer.Duplex, ct);
            doc.PrintedAt = clock.UtcNow;
            doc.PrinterId = printer.Id;
            doc.PrintError = null;
            await db.SaveChangesAsync(ct);
            return new(true, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            doc.PrintError = ex.Message;
            await db.SaveChangesAsync(ct);
            return new(false, ex.Message);
        }
    }
}
