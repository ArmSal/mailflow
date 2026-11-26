using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;
using Mailflow.Web.Pages.Settings;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Integration.Tests.Pages;

public sealed class RulesTransferTests : IDisposable
{
    private readonly SqliteFixture _fx = new();
    public void Dispose() => _fx.Dispose();

    private async Task SeedAsync()
    {
        using var db = _fx.NewDb();
        var cat = new Category { Name = "Factures", LocalSubfolder = "Factures", Color = "#112233" };
        var mb = new Mailbox { DisplayName = "Compta", Email = "compta@example.com", Host = "imap.example.com", Username = "compta", EncryptedPassword = "TOP-SECRET-PASSWORD" };
        var pr = new Printer { DisplayName = "Bureau", WindowsName = "HP-1" };
        db.AddRange(cat, mb, pr, new PrintAgent { Name = "agent", TokenHash = "TOKEN-HASH-VALUE", CreatedAt = DateTime.UtcNow });
        db.Settings.AddRange(new Setting { Key = "DocumentsRoot", Value = @"C:\secret-root" }, new Setting { Key = "PrintSenderCheck", Value = "Strict" });
        await db.SaveChangesAsync();
        db.Rules.Add(new Rule
        {
            Name = "R1", Priority = 1, MailboxId = mb.Id,
            Conditions = [new RuleCondition { Field = ConditionField.Subject, Operator = ConditionOperator.Contains, Value = "facture" }],
            Actions = [new RuleAction { Type = ActionType.SaveToCategory, CategoryId = cat.Id }, new RuleAction { Type = ActionType.Print, PrinterId = pr.Id, Copies = 2 }],
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Export_contains_no_secrets_and_round_trips()
    {
        await SeedAsync();
        string json;
        using (var db = _fx.NewDb()) json = await RulesTransfer.ExportAsync(db, default);
        Assert.DoesNotContain("TOP-SECRET-PASSWORD", json);
        Assert.DoesNotContain("TOKEN-HASH-VALUE", json);
        Assert.DoesNotContain("secret-root", json);
        var (file, error) = RulesTransfer.Parse(json);
        Assert.Null(error);

        using var db2 = _fx.NewDb();
        await RulesTransfer.ApplyAsync(db2, file!, default);
        using var db3 = _fx.NewDb();
        var rule = db3.Rules.Include(r => r.Conditions).Include(r => r.Actions).Single();
        Assert.Equal("R1", rule.Name);
        Assert.NotNull(rule.MailboxId);
        Assert.Equal(2, rule.Actions.Count);
        Assert.All(rule.Actions.Where(a => a.Type == ActionType.SaveToCategory), a => Assert.NotNull(a.CategoryId));
        Assert.Equal("TOP-SECRET-PASSWORD", db3.Mailboxes.Single().EncryptedPassword);
    }

    [Fact]
    public async Task Import_replaces_rules_and_handles_missing_mailbox_and_printer()
    {
        await SeedAsync();
        var json = """
        {"format":"mailflow-rules","version":1,"settings":{"PrintSenderCheck":"Off"},
         "categories":[{"name":"Autres","localSubfolder":"Autres","imapFolder":null,"color":"#000000","sortOrder":0}],
         "rules":[
          {"name":"A","priority":1,"isActive":true,"matchMode":"All","stopProcessing":false,"mailbox":"nobody@example.com","conditions":[],"actions":[]},
          {"name":"B","priority":2,"isActive":true,"matchMode":"Any","stopProcessing":true,"mailbox":"COMPTA@example.com","conditions":[],"actions":[{"type":"Print","category":null,"printer":"Missing","copies":1,"imapFolder":null}]},
          {"name":"C","priority":3,"isActive":true,"matchMode":"All","stopProcessing":false,"mailbox":null,"conditions":[],"actions":[{"type":"SaveToCategory","category":"Autres","printer":null,"copies":null,"imapFolder":null}]}
         ]}
        """;
        var (file, error) = RulesTransfer.Parse(json);
        Assert.Null(error);
        using var db = _fx.NewDb();
        var a = await RulesTransfer.AnalyzeAsync(db, file!, default);
        Assert.Equal(["A"], a.SkippedNoMailbox);
        Assert.Equal(["B"], a.DeactivatedNoPrinter);
        await RulesTransfer.ApplyAsync(db, file!, default);

        using var v = _fx.NewDb();
        var rules = v.Rules.Include(r => r.Actions).OrderBy(r => r.Priority).ToList();
        Assert.Equal(["B", "C"], rules.Select(r => r.Name));
        Assert.False(rules[0].IsActive);
        Assert.True(rules[1].IsActive);
        Assert.Equal("Off", v.Settings.Single(s => s.Key == "PrintSenderCheck").Value);
        Assert.Equal(["Autres"], v.Categories.Select(c => c.Name));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"format":"other","version":1,"settings":{},"categories":[],"rules":[]}""")]
    [InlineData("""{"format":"mailflow-rules","version":2,"settings":{},"categories":[],"rules":[]}""")]
    [InlineData("""{"format":"mailflow-rules","version":1,"settings":{"DocumentsRoot":"C:\x"},"categories":[],"rules":[]}""")]
    [InlineData("""{"format":"mailflow-rules","version":1,"settings":{},"categories":[],"rules":[],"extra":1}""")]
    [InlineData("""{"format":"mailflow-rules","version":1,"settings":{},"categories":[],"rules":[{"name":"x","priority":1,"isActive":true,"matchMode":"All","stopProcessing":false,"mailbox":null,"conditions":[{"field":"Nope","operator":"Contains","value":"a","caseSensitive":false}],"actions":[]}]}""")]
    [InlineData("""{"format":"mailflow-rules","version":1,"settings":{},"categories":[],"rules":[{"name":"x","priority":1,"isActive":true,"matchMode":"All","stopProcessing":false,"mailbox":null,"conditions":[{"field":"Subject","operator":"Regex","value":"(","caseSensitive":false}],"actions":[]}]}""")]
    [InlineData("""{"format":"mailflow-rules","version":1,"settings":{},"categories":[],"rules":[{"name":"x","priority":1,"isActive":true,"matchMode":"All","stopProcessing":false,"mailbox":null,"conditions":[],"actions":[{"type":"SaveToCategory","category":"Ghost","printer":null,"copies":null,"imapFolder":null}]}]}""")]
    public void Invalid_files_are_rejected(string json)
    {
        var (file, error) = RulesTransfer.Parse(json);
        Assert.Null(file);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public async Task Failed_import_changes_nothing()
    {
        await SeedAsync();
        // Passes no validation on purpose: a null condition value fails at save time.
        var bad = new RulesTransfer.FileDto("mailflow-rules", 1, [], [], [
            new("Bad", 1, true, "All", false, null, [new("Subject", "Contains", null!, false)], [])]);
        using var db = _fx.NewDb();
        await Assert.ThrowsAnyAsync<Exception>(() => RulesTransfer.ApplyAsync(db, bad, default));

        using var v = _fx.NewDb();
        Assert.Equal("R1", v.Rules.Single().Name);
        Assert.Equal("Factures", v.Categories.Single().Name);
    }
}
