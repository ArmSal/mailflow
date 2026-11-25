using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;
using Mailflow.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Mailflow.Integration.Tests.Persistence;

public sealed class PersistenceTests : IDisposable
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");

    public PersistenceTests() => _conn.Open();
    public void Dispose() => _conn.Dispose();

    private MailflowDbContext NewDb()
    {
        var db = new MailflowDbContext(new DbContextOptionsBuilder<MailflowDbContext>().UseSqlite(_conn).Options);
        db.Database.Migrate();
        return db;
    }

    [Fact]
    public async Task Migration_applies_and_seed_creates_defaults_once()
    {
        using var db = NewDb();
        await DbSeeder.SeedAsync(db, "C:\\docs");
        await DbSeeder.SeedAsync(db, "C:\\other");

        Assert.Equal("A trier", Assert.Single(db.Categories).Name);
        var rule = await db.Rules.Include(r => r.Actions).SingleAsync();
        Assert.Equal(db.Categories.Single().Id, rule.Actions.Single(a => a.CategoryId != null).CategoryId);
        Assert.Equal("C:\\docs", db.Settings.Single(s => s.Key == "DocumentsRoot").Value);
        Assert.Equal("24", db.Settings.Single(s => s.Key == "PrintMaxAgeHours").Value);
        Assert.Equal("{Category}/{yyyy}/{MM}/{yyyy-MM-dd}_{Supplier}_{OriginalName}.pdf", db.Settings.Single(s => s.Key == "FileNamePattern").Value);
    }

    [Fact]
    public async Task Upgrade_keeps_password_mailboxes_and_switches_off_microsoft_ones()
    {
        using var db = new MailflowDbContext(new DbContextOptionsBuilder<MailflowDbContext>().UseSqlite(_conn).Options);
        db.GetService<IMigrator>().Migrate("20261124200614_AddSenderVerification");
        db.Database.ExecuteSqlRaw("INSERT INTO Stores (Id, Name, IsActive) VALUES (1, 'Store A', 1)");
        var cols = "(Id, StoreId, DisplayName, Email, Host, Port, UseSsl, Username, EncryptedPassword, InboxFolder, CheckIntervalMinutes, LookbackDays, DefaultAfterProcessing, IsActive, AuthMethod) ";
        db.Database.ExecuteSqlRaw("INSERT INTO Mailboxes " + cols + "VALUES (1, 1, 'Old', 'a@example.com', 'imap.example.com', 993, 1, 'a@example.com', 'secret', 'INBOX', 5, 3, 'Leave', 1, 'Password')");
        db.Database.ExecuteSqlRaw("INSERT INTO Mailboxes " + cols + "VALUES (2, 1, 'Hot', 'b@hotmail.com', 'outlook.office365.com', 993, 1, 'b@hotmail.com', '', 'INBOX', 5, 3, 'Leave', 1, 'MicrosoftOAuth')");

        db.Database.Migrate();

        var boxes = await db.Mailboxes.OrderBy(m => m.Id).ToListAsync();
        Assert.Equal("secret", boxes[0].EncryptedPassword);
        Assert.True(boxes[0].IsActive);
        Assert.False(boxes[1].IsActive);
        Assert.Contains("Microsoft", boxes[1].LastError);
    }

    [Fact]
    public async Task Seed_does_not_touch_existing_data()
    {
        using var db = NewDb();
        db.Categories.Add(new Category { Name = "Mine", LocalSubfolder = "Mine" });
        await db.SaveChangesAsync();
        await DbSeeder.SeedAsync(db, "x");
        Assert.Equal("Mine", Assert.Single(db.Categories).Name);
    }

    [Fact]
    public async Task Document_sha256_is_unique()
    {
        using var db = NewDb();
        await SeedGraph(db);
        db.Documents.Add(new Document { ProcessedMessageId = 1, Sha256 = "abc", OriginalName = "a.pdf", StoredPath = "p", CategoryId = 1 });
        db.Documents.Add(new Document { ProcessedMessageId = 1, Sha256 = "abc", OriginalName = "b.pdf", StoredPath = "q", CategoryId = 1 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ProcessedMessage_mailbox_and_messageId_is_unique()
    {
        using var db = NewDb();
        await SeedGraph(db);
        db.ProcessedMessages.Add(new ProcessedMessage { MailboxId = 1, MessageId = "m1" });
        await db.SaveChangesAsync();
        db.ProcessedMessages.Add(new ProcessedMessage { MailboxId = 1, MessageId = "m1" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Deleting_rule_cascades_conditions_and_actions_and_enums_are_strings()
    {
        using var db = NewDb();
        db.Rules.Add(new Rule
        {
            Name = "r",
            Conditions = [new RuleCondition { Field = Domain.Enums.ConditionField.Subject, Operator = Domain.Enums.ConditionOperator.Regex, Value = "x" }],
            Actions = [new RuleAction { Type = Domain.Enums.ActionType.Print }],
        });
        await db.SaveChangesAsync();

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT Field || '/' || Operator FROM RuleConditions";
        Assert.Equal("Subject/Regex", cmd.ExecuteScalar());

        db.Rules.Remove(await db.Rules.SingleAsync());
        await db.SaveChangesAsync();
        Assert.Empty(db.RuleConditions);
        Assert.Empty(db.RuleActions);
    }

    private static async Task SeedGraph(MailflowDbContext db)
    {
        db.Mailboxes.Add(new Mailbox { DisplayName = "m", Email = "e@x.fr", Host = "h", Username = "u", EncryptedPassword = "p" });
        db.Categories.Add(new Category { Name = "c", LocalSubfolder = "c" });
        await db.SaveChangesAsync();
        db.ProcessedMessages.Add(new ProcessedMessage { MailboxId = 1, MessageId = "seed" });
        await db.SaveChangesAsync();
    }
}
