using Mailflow.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Application.Commands;

public sealed record ReclassifyResult(bool Success, string? Error);

public sealed class Reclassify(IAppDbContext db, IDocumentStore store)
{
    public async Task<ReclassifyResult> ExecuteAsync(int documentId, int categoryId, CancellationToken ct = default)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null) return new(false, "Document introuvable.");
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId, ct);
        if (category is null) return new(false, "Catégorie introuvable.");
        if (doc.DuplicateOfDocumentId is not null) return new(false, "Un doublon partage le fichier de l'original : changez la catégorie de l'original.");
        if (doc.CategoryId == categoryId) return new(true, null);

        string newPath;
        try { newPath = await store.MoveAsync(doc.StoredPath, category.LocalSubfolder, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(false, "Déplacement impossible : " + ex.Message);
        }

        doc.CategoryId = categoryId;
        doc.StoredPath = newPath;
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}
