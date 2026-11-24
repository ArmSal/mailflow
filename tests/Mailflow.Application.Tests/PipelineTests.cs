using Mailflow.Application.Abstractions;
using Mailflow.Application.Processing;
using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;
using Mailflow.Domain.Rules;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Mailflow.Application.Tests;

public sealed class TestDb : DbContext, IAppDbContext
{
    public TestDb(DbContextOptions<TestDb> options) : base(options) { }
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<Mailbox> Mailboxes => Set<Mailbox>();
    public DbSet<Printer> Printers => Set<Printer>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Rule> Rules => Set<Rule>();
    public DbSet<RuleCondition> RuleConditions => Set<RuleCondition>();
    public DbSet<RuleAction> RuleActions => Set<RuleAction>();
    public DbSet<AllowedSender> AllowedSenders => Set<AllowedSender>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<RunLog> RunLogs => Set<RunLog>();
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Setting>().HasKey(s => s.Key);
        b.Entity<Document>().HasIndex(d => d.Sha256).IsUnique();
    }
}

public sealed class Fixture : IDisposable
{
    static readonly DateTime Now = new(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

    readonly SqliteConnection _conn = new("DataSource=:memory:");
    public TestDb Db;
    public IMailClient Mail = Substitute.For<IMailClient>();
    public IPdfTextReader Pdf = Substitute.For<IPdfTextReader>();
    public IDocumentStore Store = Substitute.For<IDocumentStore>();
    public IPrinterService Printer = Substitute.For<IPrinterService>();
    public IMailboxConnectionFactory Connections = Substitute.For<IMailboxConnectionFactory>();
    public IClock Clock = Substitute.For<IClock>();
    public Mailbox Mailbox;
    public MessagePipeline Pipeline;
    public MailboxProcessor Processor;
    public MailboxConnection Conn = new("h", 993, true, "u", "p");

    public Fixture()
    {
        _conn.Open();
        Db = new TestDb(new DbContextOptionsBuilder<TestDb>().UseSqlite(_conn).Options);
        Db.Database.EnsureCreated();
        Clock.UtcNow.Returns(Now);
        Connections.CreateAsync(Arg.Any<Mailbox>(), Arg.Any<CancellationToken>())
            .Returns(ci => Conn with { Host = ci.Arg<Mailbox>().Host == "" ? Conn.Host : ci.Arg<Mailbox>().Host });
        Pdf.ReadText(Arg.Any<byte[]>(), Arg.Any<int>()).Returns("");
        Store.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(ci => new StoredDocument("C:/docs/" + ci.ArgAt<string>(3), Convert.ToHexString(ci.ArgAt<byte[]>(0) ?? []), false));

        Db.Stores.Add(new Store { Id = 1, Name = "Store A" });
        Mailbox = new Mailbox { Id = 1, StoreId = 1, Email = "owner@example.com", DefaultAfterProcessing = AfterProcessing.MarkRead };
        Db.Mailboxes.Add(Mailbox);
        Db.Categories.AddRange(new Category { Id = 1, Name = "A trier", LocalSubfolder = "A trier" },
                               new Category { Id = 2, Name = "Factures", LocalSubfolder = "Factures" });
        Db.Printers.Add(new Printer { Id = 1, StoreId = 1, WindowsName = "HP", IsActive = true });
        Db.AllowedSenders.Add(new AllowedSender { Pattern = "fournisseur.fr" });
        Db.Rules.Add(new Rule
        {
            Id = 1, Name = "Factures", Priority = 1,
            Conditions = [new RuleCondition { Field = ConditionField.Subject, Operator = ConditionOperator.Contains, Value = "facture" }],
            Actions =
            [
                new RuleAction { Type = ActionType.SaveToCategory, CategoryId = 2 },
                new RuleAction { Type = ActionType.Print, PrinterId = 1, Copies = 2 },
                new RuleAction { Type = ActionType.MarkAsRead }
            ]
        });
        Db.SaveChanges();

        Pipeline = new MessagePipeline(Db, Mail, Pdf, Store, Printer, Clock, new RuleEngine());
        Processor = new MailboxProcessor(Db, Mail, Connections, Pipeline, Clock);
    }

    public static MailMessage Msg(string id = "m1", string subject = "Facture 1", string from = "compta@fournisseur.fr",
        DateTime? received = null, params string[] pdfs) =>
        new(id, 10, received ?? Now.AddHours(-1), from, "owner@example.com", subject, "body",
            (pdfs.Length == 0 ? ["a.pdf"] : pdfs).Select(n => new MailAttachment(n, System.Text.Encoding.UTF8.GetBytes(n))).ToList());

    public void Fetch(params MailMessage[] messages) =>
        Mail.FetchSinceAsync(Arg.Any<MailboxConnection>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(messages);

    public void Dispose() { Db.Dispose(); _conn.Dispose(); }
}

public class PipelineTests
{
    [Fact]
    public async Task Matching_message_is_saved_printed_then_marked_read_in_that_order()
    {
        using var f = new Fixture();
        f.Fetch(Fixture.Msg());

        var log = await f.Processor.RunAsync(f.Mailbox, default);

        Assert.Null(log.Error);
        Assert.Equal((1, 1, 1), (log.MessagesHandled, log.DocumentsSaved, log.Printed));
        var doc = Assert.Single(f.Db.Documents);
        Assert.Equal(2, doc.CategoryId);
        Assert.NotNull(doc.PrintedAt);
        Assert.Equal(1, Assert.Single(f.Db.ProcessedMessages).MatchedRuleId);
        Received.InOrder(() =>
        {
            f.Store.SaveAsync(Arg.Any<byte[]>(), "Factures", Arg.Any<string>(), "a.pdf", Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
            f.Printer.PrintAsync(Arg.Any<string>(), "HP", 2, false, Arg.Any<CancellationToken>());
            f.Mail.MarkAsReadAsync(Arg.Any<MailboxConnection>(), "INBOX", 10, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Save_failure_records_nothing_and_does_not_mark_read_then_retry_succeeds()
    {
        using var f = new Fixture();
        f.Fetch(Fixture.Msg());
        f.Store.SaveAsync(default!, default!, default!, default!, default, default).ReturnsForAnyArgs<Task<StoredDocument>>(_ => throw new IOException("disk full"));

        var first = await f.Processor.RunAsync(f.Mailbox, default);

        Assert.NotNull(first.Error);
        Assert.Equal(0, first.MessagesHandled);
        Assert.Empty(f.Db.ProcessedMessages);
        Assert.Empty(f.Db.Documents);
        await f.Mail.DidNotReceiveWithAnyArgs().MarkAsReadAsync(default!, default!, default, default);

        f.Store.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new StoredDocument("C:/docs/a.pdf", "AA", false));
        var second = await f.Processor.RunAsync(f.Mailbox, default);

        Assert.Null(second.Error);
        Assert.Single(f.Db.ProcessedMessages);
        Assert.Single(f.Db.Documents);
        await f.Mail.Received(1).MarkAsReadAsync(Arg.Any<MailboxConnection>(), "INBOX", 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Print_failure_keeps_document_stores_error_and_still_marks_read()
    {
        using var f = new Fixture();
        f.Fetch(Fixture.Msg());
        f.Printer.PrintAsync(default!, default!, default, default, default).ReturnsForAnyArgs<Task>(_ => throw new InvalidOperationException("paper jam"));

        var log = await f.Processor.RunAsync(f.Mailbox, default);

        var doc = Assert.Single(f.Db.Documents);
        Assert.Equal("paper jam", doc.PrintError);
        Assert.Null(doc.PrintedAt);
        Assert.Equal(0, log.Printed);
        await f.Mail.Received(1).MarkAsReadAsync(Arg.Any<MailboxConnection>(), "INBOX", 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Already_processed_message_is_skipped()
    {
        using var f = new Fixture();
        f.Fetch(Fixture.Msg());
        await f.Processor.RunAsync(f.Mailbox, default);
        f.Store.ClearReceivedCalls();

        var second = await f.Processor.RunAsync(f.Mailbox, default);

        Assert.Equal(0, second.MessagesHandled);
        Assert.Single(f.Db.ProcessedMessages);
        await f.Store.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!, default!, default!, default, default);
    }

    [Fact]
    public async Task Duplicate_pdf_already_in_database_creates_no_second_document_and_no_print()
    {
        using var f = new Fixture();
        f.Fetch(Fixture.Msg("m1"), Fixture.Msg("m2"));
        f.Store.SaveAsync(default!, default!, default!, default!, default, default).ReturnsForAnyArgs(
            Task.FromResult(new StoredDocument("C:/docs/a.pdf", "SAME", false)),
            Task.FromResult(new StoredDocument("C:/docs/a.pdf", "SAME", true)));

        var log = await f.Processor.RunAsync(f.Mailbox, default);

        Assert.Equal(2, log.MessagesHandled);
        Assert.Single(f.Db.Documents);
        Assert.Equal(1, log.Printed);
        await f.Printer.ReceivedWithAnyArgs(1).PrintAsync(default!, default!, default, default, default);
    }

    [Fact]
    public async Task No_matching_rule_uses_default_rule_a_trier_no_print_leave_unread()
    {
        using var f = new Fixture();
        f.Fetch(Fixture.Msg(subject: "Hello"));

        await f.Processor.RunAsync(f.Mailbox, default);

        Assert.Equal(1, Assert.Single(f.Db.Documents).CategoryId);
        Assert.Null(Assert.Single(f.Db.ProcessedMessages).MatchedRuleId);
        await f.Printer.DidNotReceiveWithAnyArgs().PrintAsync(default!, default!, default, default, default);
        await f.Mail.DidNotReceiveWithAnyArgs().MarkAsReadAsync(default!, default!, default, default);
        await f.Mail.DidNotReceiveWithAnyArgs().MoveAsync(default!, default!, default, default!, default);
    }

    [Fact]
    public async Task Sender_not_allowed_saves_but_does_not_print()
    {
        using var f = new Fixture();
        f.Fetch(Fixture.Msg(from: "x@inconnu.com"));

        await f.Processor.RunAsync(f.Mailbox, default);

        Assert.Single(f.Db.Documents);
        await f.Printer.DidNotReceiveWithAnyArgs().PrintAsync(default!, default!, default, default, default);
        await f.Mail.Received(1).MarkAsReadAsync(Arg.Any<MailboxConnection>(), "INBOX", 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Mailbox_action_failure_does_not_lose_the_record()
    {
        using var f = new Fixture();
        f.Fetch(Fixture.Msg());
        f.Mail.MarkAsReadAsync(default!, default!, default, default).ReturnsForAnyArgs<Task>(_ => throw new IOException("imap down"));

        var log = await f.Processor.RunAsync(f.Mailbox, default);

        Assert.Single(f.Db.ProcessedMessages);
        Assert.Contains("imap down", log.Error);
    }

    [Fact]
    public async Task Skip_action_records_message_without_saving()
    {
        using var f = new Fixture();
        f.Db.Rules.Add(new Rule
        {
            Id = 2, Name = "skip", Priority = 0,
            Conditions = [new RuleCondition { Field = ConditionField.Subject, Operator = ConditionOperator.Contains, Value = "pub" }],
            Actions = [new RuleAction { Type = ActionType.Skip }]
        });
        f.Db.SaveChanges();
        f.Fetch(Fixture.Msg(subject: "pub"));

        await f.Processor.RunAsync(f.Mailbox, default);

        Assert.Equal(MessageStatus.Skipped, Assert.Single(f.Db.ProcessedMessages).Status);
        Assert.Empty(f.Db.Documents);
        await f.Mail.DidNotReceiveWithAnyArgs().MarkAsReadAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task One_mailbox_failing_does_not_stop_the_others()
    {
        using var f = new Fixture();
        f.Db.Mailboxes.Add(new Mailbox { Id = 2, StoreId = 1, Host = "bad", DefaultAfterProcessing = AfterProcessing.Leave });
        f.Db.SaveChanges();
        f.Mail.FetchSinceAsync(Arg.Is<MailboxConnection>(c => c.Host == "bad"), default!, default, default).ReturnsForAnyArgs<Task<IReadOnlyList<MailMessage>>>(ci =>
            ((MailboxConnection)ci[0]).Host == "bad" ? throw new IOException("unreachable") : Task.FromResult<IReadOnlyList<MailMessage>>([Fixture.Msg()]));
        f.Mailbox.Host = "good";

        var logs = await f.Processor.RunAllAsync(default);

        Assert.Equal(2, logs.Count);
        Assert.Contains("unreachable", logs.Single(l => l.MailboxId == 2).Error);
        Assert.Null(logs.Single(l => l.MailboxId == 1).Error);
        Assert.Equal(2, f.Db.RunLogs.Count());
        Assert.Contains("unreachable", f.Db.Mailboxes.Single(m => m.Id == 2).LastError);
        Assert.Single(f.Db.Documents);
    }
}

public class PrintPolicyTests
{
    static readonly DateTime Now = new(2026, 1, 10, 12, 0, 0);
    static readonly Printer Active = new() { Id = 1, IsActive = true };
    static readonly AllowedSender[] Allowed = [new() { Pattern = "fournisseur.fr" }, new() { Pattern = "Boss@autre.com" }, new() { Pattern = "magasin2.fr", StoreId = 2 }];

    static bool Check(bool rule = true, string from = "a@fournisseur.fr", double ageHours = 1, Printer? printer = null, int store = 1, int maxAge = 48) =>
        PrintPolicy.Allows(rule, from, Allowed, store, Now.AddHours(-ageHours), Now, maxAge, printer ?? Active);

    [Fact] public void All_conditions_met() => Assert.True(Check());
    [Fact] public void Rule_without_print_is_refused() => Assert.False(Check(rule: false));
    [Fact] public void Unknown_sender_is_refused() => Assert.False(Check(from: "a@inconnu.com"));
    [Fact] public void Exact_address_pattern_is_case_insensitive() => Assert.True(Check(from: "boss@AUTRE.com"));
    [Fact] public void Other_address_at_pattern_domain_is_refused_for_address_pattern() => Assert.False(Check(from: "x@autre.com"));
    [Fact] public void Message_older_than_max_age_is_refused() => Assert.False(Check(ageHours: 49));
    [Fact] public void Message_exactly_at_max_age_is_allowed() => Assert.True(Check(ageHours: 48));
    [Fact] public void Inactive_printer_is_refused() => Assert.False(Check(printer: new Printer { IsActive = false }));
    [Fact] public void Missing_printer_is_refused() => Assert.False(PrintPolicy.Allows(true, "a@fournisseur.fr", Allowed, 1, Now, Now, 48, null));
    [Fact] public void Sender_scoped_to_other_store_is_refused() => Assert.False(Check(from: "a@magasin2.fr", store: 1));
    [Fact] public void Sender_scoped_to_this_store_is_allowed() => Assert.True(Check(from: "a@magasin2.fr", store: 2));
}

public class SenderCheckTests
{
    const string Blocked = "Impression bloquée : expéditeur non vérifié (DKIM/DMARC).";

    static Fixture Setup(string? mode, SenderVerification v)
    {
        var f = new Fixture();
        if (mode != null) f.Db.Settings.Add(new Setting { Key = "PrintSenderCheck", Value = mode });
        f.Db.SaveChanges();
        f.Fetch(Fixture.Msg() with { SenderVerification = v });
        return f;
    }

    [Theory]
    [InlineData("Off", SenderVerification.Unknown, true)]
    [InlineData("Off", SenderVerification.Pass, true)]
    [InlineData("Off", SenderVerification.Fail, true)]
    [InlineData("Warn", SenderVerification.Unknown, true)]
    [InlineData("Warn", SenderVerification.Pass, true)]
    [InlineData("Warn", SenderVerification.Fail, true)]
    [InlineData("Strict", SenderVerification.Unknown, false)]
    [InlineData("Strict", SenderVerification.Pass, true)]
    [InlineData("Strict", SenderVerification.Fail, false)]
    public async Task Mode_and_verification_decide_printing(string mode, SenderVerification v, bool prints)
    {
        using var f = Setup(mode, v);

        var log = await f.Processor.RunAsync(f.Mailbox, default);

        var doc = Assert.Single(f.Db.Documents);
        Assert.Equal(prints ? 1 : 0, log.Printed);
        Assert.Equal(1, log.DocumentsSaved);
        Assert.Equal(v, Assert.Single(f.Db.ProcessedMessages).SenderVerification);
        if (prints)
        {
            Assert.NotNull(doc.PrintedAt);
            Assert.Null(doc.PrintError);
        }
        else
        {
            await f.Printer.DidNotReceiveWithAnyArgs().PrintAsync(default!, default!, default, default, default);
            Assert.Null(doc.PrintedAt);
            Assert.Equal(Blocked, doc.PrintError);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("banana")]
    [InlineData("7")]
    public async Task Missing_or_invalid_setting_defaults_to_Warn_and_prints(string? mode)
    {
        using var f = Setup(mode, SenderVerification.Fail);

        var log = await f.Processor.RunAsync(f.Mailbox, default);

        Assert.Equal(1, log.Printed);
    }

    [Fact]
    public async Task Strict_block_still_marks_the_message_read()
    {
        using var f = Setup("Strict", SenderVerification.Fail);

        await f.Processor.RunAsync(f.Mailbox, default);

        await f.Mail.Received(1).MarkAsReadAsync(Arg.Any<MailboxConnection>(), "INBOX", 10, Arg.Any<CancellationToken>());
    }

    [Fact] public void ParseMode_is_case_insensitive() => Assert.Equal(SenderCheckMode.Strict, PrintPolicy.ParseMode(" strict "));

    [Fact]
    public void Only_strict_without_pass_blocks()
    {
        foreach (var m in Enum.GetValues<SenderCheckMode>())
            foreach (var v in Enum.GetValues<SenderVerification>())
                Assert.Equal(m == SenderCheckMode.Strict && v != SenderVerification.Pass, PrintPolicy.SenderBlocksPrint(m, v));
    }
}
