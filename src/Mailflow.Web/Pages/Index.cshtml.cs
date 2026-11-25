using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Mailflow.Web.Worker;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Web.Pages;

public class IndexModel(IAppDbContext db, WorkerState state, RunNowSignal signal, IClock clock) : PageModel
{
    public record Counters(int Messages, int Documents, int Printed, int Errors);
    public record MailboxError(string Name, DateTime? At, string Error, string? Hint = null);
    public record RunError(DateTime At, string Mailbox, string Error);
    public record LatestDocument(string Name, string Category, DateTime? PrintedAt, string? PrintError);

    public WorkerState State => state;
    public Counters Today { get; private set; } = new(0, 0, 0, 0);
    public List<MailboxError> MailboxErrors { get; private set; } = [];
    public List<RunError> RunErrors { get; private set; } = [];
    public List<LatestDocument> Documents { get; private set; } = [];

    public string StatusLabel => state.IsPaused ? "En pause" : state.IsRunning ? "En cours" : "En attente";
    public string StatusClass => state.IsPaused ? "bg-warning text-dark" : state.IsRunning ? "bg-primary" : "bg-success";

    public Task OnGetAsync() => LoadAsync();

    public async Task<IActionResult> OnGetStatusAsync()
    {
        await LoadAsync();
        return Partial("_StatusBlock", this);
    }

    public async Task<IActionResult> OnPostRunNowAsync()
    {
        signal.Request();
        await LoadAsync();
        return Partial("_StatusBlock", this);
    }

    public async Task<IActionResult> OnPostPauseAsync()
    {
        state.Pause();
        await LoadAsync();
        return Partial("_StatusBlock", this);
    }

    public async Task<IActionResult> OnPostResumeAsync()
    {
        state.Resume();
        await LoadAsync();
        return Partial("_StatusBlock", this);
    }

    private async Task LoadAsync()
    {
        // "Today" is the local calendar day; log rows are stored in UTC.
        var dayStart = clock.UtcNow.ToLocalTime().Date.ToUniversalTime();
        var runs = await db.RunLogs.Where(r => r.StartedAt >= dayStart).ToListAsync();
        Today = new Counters(runs.Sum(r => r.MessagesHandled), runs.Sum(r => r.DocumentsSaved),
            runs.Sum(r => r.Printed), runs.Count(r => r.Error != null));

        MailboxErrors = (await db.Mailboxes.Where(m => m.LastError != null).OrderBy(m => m.DisplayName).ToListAsync())
            .Select(m => new MailboxError(m.DisplayName, m.LastCheckAt, m.LastError!, Mailflow.Web.Pages.Mailboxes.MailErrorHints.For(m.LastError, m.Host))).ToList();

        var names = await db.Mailboxes.ToDictionaryAsync(m => m.Id, m => m.DisplayName);
        RunErrors = (await db.RunLogs.Where(r => r.Error != null).OrderByDescending(r => r.StartedAt).Take(5).ToListAsync())
            .Select(r => new RunError(r.StartedAt, r.MailboxId is int id && names.TryGetValue(id, out var n) ? n : "-", r.Error!)).ToList();

        var categories = await db.Categories.ToDictionaryAsync(c => c.Id, c => c.Name);
        Documents = (await db.Documents.OrderByDescending(d => d.Id).Take(10).ToListAsync())
            .Select(d => new LatestDocument(d.OriginalName, categories.GetValueOrDefault(d.CategoryId, "-"), d.PrintedAt, d.PrintError)).ToList();
    }
}
