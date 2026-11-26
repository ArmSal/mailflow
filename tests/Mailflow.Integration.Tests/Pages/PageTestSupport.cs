using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Integration.Tests.Pages;

public sealed class TestDb : DbContext, IAppDbContext
{
    public TestDb(DbContextOptions<TestDb> o) : base(o) { }
    public DbSet<Mailbox> Mailboxes { get; set; } = null!;
    public DbSet<Printer> Printers { get; set; } = null!;
    public DbSet<Category> Categories { get; set; } = null!;
    public DbSet<Rule> Rules { get; set; } = null!;
    public DbSet<RuleCondition> RuleConditions { get; set; } = null!;
    public DbSet<RuleAction> RuleActions { get; set; } = null!;
    public DbSet<ProcessedMessage> ProcessedMessages { get; set; } = null!;
    public DbSet<Document> Documents { get; set; } = null!;
    public DbSet<RunLog> RunLogs { get; set; } = null!;
    public DbSet<Setting> Settings { get; set; } = null!;
    public DbSet<PrintAgent> PrintAgents { get; set; } = null!;
    public DbSet<PrintJob> PrintJobs { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder b) => b.Entity<Setting>().HasKey(s => s.Key);
}

public sealed class SqliteFixture : IDisposable
{
    private readonly SqliteConnection _conn = new("DataSource=:memory:");
    private readonly DbContextOptions<TestDb> _options;

    public SqliteFixture()
    {
        _conn.Open();
        _options = new DbContextOptionsBuilder<TestDb>().UseSqlite(_conn).Options;
        using var db = new TestDb(_options);
        db.Database.EnsureCreated();
    }

    public TestDb NewDb() => new(_options);
    public void Dispose() => _conn.Dispose();
}

/// <summary>Reversible fake: "enc:" + plain.</summary>
public sealed class FakeSecretStore : ISecretStore
{
    public string Protect(string plainText) => "enc:" + plainText;
    public string Unprotect(string protectedText) => protectedText["enc:".Length..];
}

public sealed class FakeMailClient : IMailClient
{
    public MailboxConnection? Last { get; private set; }
    public Task<ConnectionTestResult> TestConnectionAsync(MailboxConnection connection, CancellationToken ct)
    {
        Last = connection;
        return Task.FromResult(new ConnectionTestResult(true, 12, ["INBOX", "Sent"], null));
    }
    public Task<IReadOnlyList<MailMessage>> FetchSinceAsync(MailboxConnection c, string f, DateTime s, CancellationToken ct) => throw new NotSupportedException();
    public Task MarkAsReadAsync(MailboxConnection c, string f, uint uid, CancellationToken ct) => throw new NotSupportedException();
    public Task MoveAsync(MailboxConnection c, string f, uint uid, string t, CancellationToken ct) => throw new NotSupportedException();
}

public static class PageHelpers
{
    public static T With<T>(this T page) where T : PageModel
    {
        page.PageContext = new PageContext
        {
            ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(
                new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(),
                new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary()),
        };
        page.MetadataProvider = new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider();
        return page;
    }
}
