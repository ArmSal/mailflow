using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Web.Pages.Journal;

public class IndexModel(IAppDbContext db) : PageModel
{
    public const int PageSize = 25;

    public record Row(RunLog Log, string Mailbox);

    [BindProperty(SupportsGet = true)] public bool ErrorsOnly { get; set; }
    [BindProperty(SupportsGet = true)] public int? MailboxId { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;

    public List<Row> Rows { get; private set; } = [];
    public List<Mailbox> Mailboxes { get; private set; } = [];
    public int TotalPages { get; private set; } = 1;

    public async Task OnGetAsync()
    {
        Mailboxes = await db.Mailboxes.OrderBy(m => m.DisplayName).ToListAsync();
        var query = db.RunLogs.AsQueryable();
        if (ErrorsOnly) query = query.Where(r => r.Error != null);
        if (MailboxId != null) query = query.Where(r => r.MailboxId == MailboxId);

        var total = await query.CountAsync();
        TotalPages = Math.Max(1, (total + PageSize - 1) / PageSize);
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);
        var logs = await query.OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id)
            .Skip((PageNumber - 1) * PageSize).Take(PageSize).ToListAsync();
        var names = Mailboxes.ToDictionary(m => m.Id, m => m.DisplayName);
        Rows = logs.Select(l => new Row(l, l.MailboxId is int id ? names.GetValueOrDefault(id, "-") : "-")).ToList();
    }

    public static string FormatDuration(TimeSpan d) =>
        d.TotalSeconds < 1 ? $"{d.TotalMilliseconds:0} ms" : d.TotalMinutes < 1 ? $"{d.TotalSeconds:0.0} s" : $"{(int)d.TotalMinutes} min {d.Seconds} s";
}
