using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Mailflow.Integration.Tests.Pages;
using Mailflow.Web.Worker;
using Microsoft.AspNetCore.Mvc;
using DashboardModel = Mailflow.Web.Pages.IndexModel;
using JournalModel = Mailflow.Web.Pages.Journal.IndexModel;

namespace Mailflow.Integration.Tests.Ops;

public class OpsPageTests : IDisposable
{
    private sealed class FixedClock(DateTime utc) : IClock { public DateTime UtcNow => utc; }

    private static readonly DateTime Now = DateTime.UtcNow;
    private readonly SqliteFixture _fx = new();
    private readonly WorkerState _state = new();
    private readonly RunNowSignal _signal = new();

    public void Dispose() => _fx.Dispose();

    private DashboardModel Dashboard(TestDb db) => new DashboardModel(db, _state, _signal, new FixedClock(Now)).With();

    private static RunLog Run(DateTime at, int mailbox = 1, int msgs = 0, int docs = 0, int printed = 0, string? error = null) =>
        new() { StartedAt = at, MailboxId = mailbox, MessagesHandled = msgs, DocumentsSaved = docs, Printed = printed, Error = error, Duration = TimeSpan.FromSeconds(2) };

    [Fact]
    public async Task Counters_sum_today_only()
    {
        using var db = _fx.NewDb();
        var localMidnight = Now.ToLocalTime().Date.ToUniversalTime();
        db.RunLogs.AddRange(
            Run(localMidnight.AddMinutes(1), msgs: 2, docs: 3, printed: 1),
            Run(localMidnight.AddMinutes(2), msgs: 1, docs: 1, printed: 1, error: "boom"),
            Run(localMidnight.AddDays(-1).AddHours(5), msgs: 50, docs: 50, printed: 50, error: "old"));
        db.SaveChanges();

        var page = Dashboard(db);
        await page.OnGetAsync();

        Assert.Equal(new DashboardModel.Counters(3, 4, 2, 1), page.Today);
    }

    [Fact]
    public async Task Errors_and_latest_documents_are_listed()
    {
        using var db = _fx.NewDb();
        db.Mailboxes.AddRange(new Mailbox { Id = 1, DisplayName = "Compta", LastError = "login refusé" }, new Mailbox { Id = 2, DisplayName = "Ok" });
        db.Categories.Add(new Category { Id = 1, Name = "Factures" });
        db.RunLogs.Add(Run(Now, error: "Mailbox error: x"));
        for (var i = 1; i <= 12; i++)
            db.Documents.Add(new Document { Id = i, OriginalName = $"d{i}.pdf", CategoryId = 1, PrintError = i == 12 ? "bourrage" : null, Sha256 = "h" + i });
        db.SaveChanges();

        var page = Dashboard(db);
        await page.OnGetAsync();

        Assert.Equal("login refusé", Assert.Single(page.MailboxErrors).Error);
        Assert.Equal("Compta", Assert.Single(page.RunErrors).Mailbox);
        Assert.Equal(10, page.Documents.Count);
        Assert.Equal("d12.pdf", page.Documents[0].Name);
        Assert.Equal("bourrage", page.Documents[0].PrintError);
        Assert.Equal("Factures", page.Documents[0].Category);
    }

    [Fact]
    public async Task Clear_errors_removes_mailbox_and_run_errors_but_keeps_ok_runs()
    {
        using var db = _fx.NewDb();
        db.Mailboxes.Add(new Mailbox { DisplayName = "M", Email = "a@b.fr", Host = "h", LastError = "boom" });
        db.RunLogs.AddRange(Run(Now, msgs: 2, error: "bad"), Run(Now, msgs: 3));
        db.SaveChanges();

        var page = Dashboard(db);
        Assert.IsType<PartialViewResult>(await page.OnPostClearErrorsAsync());

        Assert.Empty(page.MailboxErrors);
        Assert.Empty(page.RunErrors);
        Assert.Null(db.Mailboxes.Single().LastError);
        Assert.Equal(3, db.RunLogs.Single().MessagesHandled);
    }

    [Fact]
    public async Task Pause_resume_and_run_now_call_state_and_signal()
    {
        using var db = _fx.NewDb();
        var page = Dashboard(db);

        Assert.IsType<PartialViewResult>(await page.OnPostPauseAsync());
        Assert.True(_state.IsPaused);
        Assert.Equal("En pause", page.StatusLabel);
        await page.OnPostResumeAsync();
        Assert.False(_state.IsPaused);

        await page.OnPostRunNowAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await _signal.WaitAsync(cts.Token); // completes only if Request() was called
    }

    [Fact]
    public async Task Journal_paged_newest_first_with_filters()
    {
        using var db = _fx.NewDb();
        db.Mailboxes.AddRange(new Mailbox { Id = 1, DisplayName = "A" }, new Mailbox { Id = 2, DisplayName = "B" });
        for (var i = 0; i < 30; i++) db.RunLogs.Add(Run(Now.AddMinutes(-i), mailbox: 1));
        db.RunLogs.Add(Run(Now.AddHours(-5), mailbox: 2, error: "<b>x</b>"));
        db.SaveChanges();

        var p1 = new JournalModel(db).With();
        await p1.OnGetAsync();
        Assert.Equal(25, p1.Rows.Count);
        Assert.Equal(2, p1.TotalPages);
        Assert.True(p1.Rows[0].Log.StartedAt > p1.Rows[1].Log.StartedAt);

        var p2 = new JournalModel(db) { PageNumber = 2 }.With();
        await p2.OnGetAsync();
        Assert.Equal(6, p2.Rows.Count);

        var errs = new JournalModel(db) { ErrorsOnly = true }.With();
        await errs.OnGetAsync();
        Assert.Equal("<b>x</b>", Assert.Single(errs.Rows).Log.Error);

        var box = new JournalModel(db) { MailboxId = 2 }.With();
        await box.OnGetAsync();
        Assert.Equal("B", Assert.Single(box.Rows).Mailbox);
    }
}
