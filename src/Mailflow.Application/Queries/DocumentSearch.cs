using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Application.Queries;

public sealed record DocumentSearchFilter(
    DateOnly? From = null,
    DateOnly? To = null,
    string? Supplier = null,
    int? CategoryId = null,
    int? StoreId = null,
    bool PrintErrorsOnly = false,
    int Page = 1,
    bool UnverifiedOnly = false);

public sealed record DocumentRow(
    int DocumentId,
    DateTime ReceivedAt,
    string From,
    string Subject,
    string OriginalName,
    int CategoryId,
    string Category,
    int StoreId,
    string Store,
    DateTime? PrintedAt,
    string? PrintError,
    SenderVerification SenderVerification = SenderVerification.Unknown);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}

public sealed class DocumentSearch(IAppDbContext db)
{
    public const int PageSize = 25;

    private IQueryable<Joined> Joins() =>
        from d in db.Documents
        join pm in db.ProcessedMessages on d.ProcessedMessageId equals pm.Id
        join c in db.Categories on d.CategoryId equals c.Id
        join mb in db.Mailboxes on pm.MailboxId equals mb.Id
        join st in db.Stores on mb.StoreId equals st.Id
        select new Joined { D = d, Pm = pm, C = c, St = st };

    private sealed class Joined
    {
        public Document D { get; init; } = null!;
        public ProcessedMessage Pm { get; init; } = null!;
        public Category C { get; init; } = null!;
        public Store St { get; init; } = null!;
    }

    private static DocumentRow ToRow(Joined j) => new(j.D.Id, j.Pm.ReceivedAt, j.Pm.From, j.Pm.Subject, j.D.OriginalName,
        j.C.Id, j.C.Name, j.St.Id, j.St.Name, j.D.PrintedAt, j.D.PrintError, j.Pm.SenderVerification);

    public async Task<DocumentRow?> GetRowAsync(int documentId, CancellationToken ct = default)
    {
        var j = await Joins().Where(x => x.D.Id == documentId).FirstOrDefaultAsync(ct);
        return j is null ? null : ToRow(j);
    }

    public async Task<PagedResult<DocumentRow>> SearchAsync(DocumentSearchFilter f, CancellationToken ct = default)
    {
        var q = Joins();
        if (f.From is { } from) { var min = from.ToDateTime(TimeOnly.MinValue); q = q.Where(r => r.Pm.ReceivedAt >= min); }
        if (f.To is { } to) { var max = to.AddDays(1).ToDateTime(TimeOnly.MinValue); q = q.Where(r => r.Pm.ReceivedAt < max); }
        if (!string.IsNullOrWhiteSpace(f.Supplier))
        {
            var s = f.Supplier.Trim().ToLower();
            q = q.Where(r => r.Pm.From.ToLower().Contains(s));
        }
        if (f.CategoryId is { } cat) q = q.Where(r => r.C.Id == cat);
        if (f.StoreId is { } store) q = q.Where(r => r.St.Id == store);
        if (f.PrintErrorsOnly) q = q.Where(r => r.D.PrintError != null && r.D.PrintError != "");

        if (f.UnverifiedOnly) q = q.Where(r => r.Pm.SenderVerification != SenderVerification.Pass);

        var total = await q.CountAsync(ct);
        var page = Math.Max(1, f.Page);
        var items = await q.OrderByDescending(r => r.Pm.ReceivedAt).ThenByDescending(r => r.D.Id)
            .Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(ct);
        return new PagedResult<DocumentRow>(items.Select(ToRow).ToList(), total, page, PageSize);
    }
}
