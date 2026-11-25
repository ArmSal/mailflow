using Mailflow.Application.Abstractions;
using Mailflow.Application.Commands;
using Mailflow.Application.Queries;
using Mailflow.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Mailflow.Application.Tests.History;

public sealed class HistoryDb : DbContext, IAppDbContext
{
    public HistoryDb(DbContextOptions<HistoryDb> o) : base(o) { }
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
    protected override void OnModelCreating(ModelBuilder b) => b.Entity<Setting>().HasKey(s => s.Key);
}

public sealed class HistoryFixture : IDisposable
{
    private readonly SqliteConnection _conn = new("DataSource=:memory:");
    private readonly DbContextOptions<HistoryDb> _options;

    public HistoryFixture()
    {
        _conn.Open();
        _options = new DbContextOptionsBuilder<HistoryDb>().UseSqlite(_conn).Options;
        using var db = new HistoryDb(_options);
        db.Database.EnsureCreated();
        db.Mailboxes.AddRange(new Mailbox { Id = 1, Email = "a@x.fr" }, new Mailbox { Id = 2, Email = "b@x.fr" });
        db.Categories.AddRange(
            new Category { Id = 1, Name = "Factures", LocalSubfolder = "Factures" },
            new Category { Id = 2, Name = "BL", LocalSubfolder = "BL" });
        db.Printers.AddRange(
            new Printer { Id = 1, DisplayName = "P1", WindowsName = "Win1", DefaultCopies = 2, Duplex = true },
            new Printer { Id = 2, DisplayName = "P2", WindowsName = "Win2" },
            new Printer { Id = 3, DisplayName = "P3", WindowsName = "Win3", IsActive = false });
        db.SaveChanges();
    }

    public HistoryDb NewDb() => new(_options);
    public void Dispose() => _conn.Dispose();

    /// <summary>Adds a message + document; returns the document id.</summary>
    public int AddDoc(int mailboxId, DateTime receivedAt, string from, int categoryId = 1, string? printError = null, int? printerId = null, string path = "C:/docs/Factures/a.pdf", Mailflow.Domain.Enums.SenderVerification verification = Mailflow.Domain.Enums.SenderVerification.Unknown)
    {
        using var db = NewDb();
        var pm = new ProcessedMessage { MailboxId = mailboxId, MessageId = Guid.NewGuid().ToString(), ReceivedAt = receivedAt, From = from, Subject = "S", ProcessedAt = receivedAt, SenderVerification = verification };
        db.ProcessedMessages.Add(pm);
        db.SaveChanges();
        var d = new Document { ProcessedMessageId = pm.Id, Sha256 = "x", OriginalName = "f.pdf", StoredPath = path, CategoryId = categoryId, PrintError = printError, PrinterId = printerId };
        db.Documents.Add(d);
        db.SaveChanges();
        return d.Id;
    }
}

public class DocumentSearchTests
{
    private static readonly DateTime D = new(2026, 3, 10, 12, 0, 0);

    [Fact]
    public async Task Filters_by_date_range_inclusive_of_end_day()
    {
        using var fx = new HistoryFixture();
        fx.AddDoc(1, D.AddDays(-5), "a@f.fr");
        var inRange = fx.AddDoc(1, D, "a@f.fr");
        var lastDay = fx.AddDoc(1, D.AddDays(2).AddHours(10), "a@f.fr");
        fx.AddDoc(1, D.AddDays(9), "a@f.fr");
        using var db = fx.NewDb();
        var r = await new DocumentSearch(db).SearchAsync(new(From: DateOnly.FromDateTime(D), To: DateOnly.FromDateTime(D.AddDays(2))));
        Assert.Equal([lastDay, inRange], r.Items.Select(i => i.DocumentId));
    }

    [Fact]
    public async Task Filters_by_supplier_case_insensitive()
    {
        using var fx = new HistoryFixture();
        var hit = fx.AddDoc(1, D, "Compta@Fournisseur-X.fr");
        fx.AddDoc(1, D, "other@y.fr");
        using var db = fx.NewDb();
        var r = await new DocumentSearch(db).SearchAsync(new(Supplier: "fournisseur-x"));
        Assert.Equal(hit, Assert.Single(r.Items).DocumentId);
    }

    [Fact]
    public async Task Filters_by_category_and_errors()
    {
        using var fx = new HistoryFixture();
        fx.AddDoc(1, D, "a@f.fr", categoryId: 1);
        var cat2 = fx.AddDoc(1, D, "a@f.fr", categoryId: 2);
        var err = fx.AddDoc(1, D, "a@f.fr", printError: "boom");
        using var db = fx.NewDb();
        var s = new DocumentSearch(db);
        Assert.Equal(cat2, Assert.Single((await s.SearchAsync(new(CategoryId: 2))).Items).DocumentId);
        var e = Assert.Single((await s.SearchAsync(new(PrintErrorsOnly: true))).Items);
        Assert.Equal(err, e.DocumentId);
        Assert.Equal("boom", e.PrintError);
        Assert.Equal("Factures", e.Category);
    }

    [Fact]
    public async Task Rows_expose_sender_verification_and_unverified_filter_excludes_only_pass()
    {
        using var fx = new HistoryFixture();
        var t = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);
        var pass = fx.AddDoc(1, t, "a@supplier.example", verification: Mailflow.Domain.Enums.SenderVerification.Pass);
        var unknown = fx.AddDoc(1, t.AddMinutes(1), "b@supplier.example");
        var fail = fx.AddDoc(1, t.AddMinutes(2), "c@supplier.example", verification: Mailflow.Domain.Enums.SenderVerification.Fail);
        using var db = fx.NewDb();
        var s = new DocumentSearch(db);

        var all = (await s.SearchAsync(new())).Items.ToDictionary(r => r.DocumentId, r => r.SenderVerification);
        Assert.Equal(Mailflow.Domain.Enums.SenderVerification.Pass, all[pass]);
        Assert.Equal(Mailflow.Domain.Enums.SenderVerification.Unknown, all[unknown]);
        Assert.Equal(Mailflow.Domain.Enums.SenderVerification.Fail, all[fail]);
        Assert.Equal(Mailflow.Domain.Enums.SenderVerification.Fail, (await s.GetRowAsync(fail))!.SenderVerification);

        var unverified = (await s.SearchAsync(new(UnverifiedOnly: true))).Items.Select(r => r.DocumentId).ToHashSet();
        Assert.Equal(new[] { unknown, fail }.ToHashSet(), unverified);
    }

    [Fact]
    public async Task Pages_25_newest_first()
    {
        using var fx = new HistoryFixture();
        for (var i = 0; i < 30; i++) fx.AddDoc(1, D.AddMinutes(i), "a@f.fr");
        using var db = fx.NewDb();
        var s = new DocumentSearch(db);
        var p1 = await s.SearchAsync(new(Page: 1));
        var p2 = await s.SearchAsync(new(Page: 2));
        Assert.Equal(30, p1.Total);
        Assert.Equal(2, p1.PageCount);
        Assert.Equal(25, p1.Items.Count);
        Assert.Equal(5, p2.Items.Count);
        Assert.Equal(D.AddMinutes(29), p1.Items[0].ReceivedAt);
        Assert.Equal(D.AddMinutes(4), p2.Items[0].ReceivedAt);
    }
}

public class ReprintTests
{
    private static readonly DateTime Now = new(2026, 4, 1, 8, 0, 0, DateTimeKind.Utc);

    private static (Reprint cmd, IPrinterService ps) Make(HistoryDb db)
    {
        var ps = Substitute.For<IPrinterService>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return (new Reprint(db, ps, clock), ps);
    }

    [Fact]
    public async Task Success_updates_document_and_uses_chosen_printer_settings()
    {
        using var fx = new HistoryFixture();
        var id = fx.AddDoc(1, Now, "a@f.fr", printError: "old", printerId: 2);
        using var db = fx.NewDb();
        var (cmd, ps) = Make(db);

        var r = await cmd.ExecuteAsync(id, 1);

        Assert.True(r.Success);
        await ps.Received(1).PrintAsync("C:/docs/Factures/a.pdf", "Win1", 2, true, Arg.Any<CancellationToken>());
        using var check = fx.NewDb();
        var d = check.Documents.Single();
        Assert.Equal(Now, d.PrintedAt);
        Assert.Equal(1, d.PrinterId);
        Assert.Null(d.PrintError);
    }

    [Fact]
    public async Task Defaults_to_previous_printer()
    {
        using var fx = new HistoryFixture();
        var id = fx.AddDoc(1, Now, "a@f.fr", printerId: 2);
        using var db = fx.NewDb();
        var (cmd, ps) = Make(db);
        await cmd.ExecuteAsync(id, null);
        await ps.Received(1).PrintAsync(Arg.Any<string>(), "Win2", 1, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Defaults_to_first_active_printer_of_store_when_never_printed()
    {
        using var fx = new HistoryFixture();
        var id = fx.AddDoc(1, Now, "a@f.fr");
        using var db = fx.NewDb();
        var (cmd, ps) = Make(db);
        await cmd.ExecuteAsync(id, null);
        await ps.Received(1).PrintAsync(Arg.Any<string>(), "Win1", Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Failure_stores_error_and_does_not_throw()
    {
        using var fx = new HistoryFixture();
        var id = fx.AddDoc(1, Now, "a@f.fr");
        using var db = fx.NewDb();
        var (cmd, ps) = Make(db);
        ps.PrintAsync(default!, default!, default, default, default).ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("paper jam")));

        var r = await cmd.ExecuteAsync(id, 1);

        Assert.False(r.Success);
        using var check = fx.NewDb();
        var d = check.Documents.Single();
        Assert.Equal("paper jam", d.PrintError);
        Assert.Null(d.PrintedAt);
    }

    [Fact]
    public async Task Inactive_printer_is_rejected_without_printing()
    {
        using var fx = new HistoryFixture();
        var id = fx.AddDoc(1, Now, "a@f.fr");
        using var db = fx.NewDb();
        var (cmd, ps) = Make(db);
        var r = await cmd.ExecuteAsync(id, 3);
        Assert.False(r.Success);
        await ps.DidNotReceiveWithAnyArgs().PrintAsync(default!, default!, default, default, default);
    }
}

public class ReclassifyTests
{
    [Fact]
    public async Task Success_updates_category_and_stored_path_with_returned_path()
    {
        using var fx = new HistoryFixture();
        var id = fx.AddDoc(1, DateTime.UtcNow, "a@f.fr");
        using var db = fx.NewDb();
        var store = Substitute.For<IDocumentStore>();
        store.MoveAsync("C:/docs/Factures/a.pdf", "BL", Arg.Any<CancellationToken>()).Returns("C:/docs/BL/a (2).pdf");

        var r = await new Reclassify(db, store).ExecuteAsync(id, 2);

        Assert.True(r.Success);
        using var check = fx.NewDb();
        var d = check.Documents.Single();
        Assert.Equal(2, d.CategoryId);
        Assert.Equal("C:/docs/BL/a (2).pdf", d.StoredPath);
    }

    [Fact]
    public async Task Move_failure_leaves_database_unchanged()
    {
        using var fx = new HistoryFixture();
        var id = fx.AddDoc(1, DateTime.UtcNow, "a@f.fr");
        using var db = fx.NewDb();
        var store = Substitute.For<IDocumentStore>();
        store.MoveAsync(default!, default!, default).ThrowsAsyncForAnyArgs(new IOException("locked"));

        var r = await new Reclassify(db, store).ExecuteAsync(id, 2);

        Assert.False(r.Success);
        using var check = fx.NewDb();
        var d = check.Documents.Single();
        Assert.Equal(1, d.CategoryId);
        Assert.Equal("C:/docs/Factures/a.pdf", d.StoredPath);
    }
}
