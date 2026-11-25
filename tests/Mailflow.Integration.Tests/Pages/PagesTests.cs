using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;
using Mailflow.Web.Pages.Categories;
using Mailflow.Web.Pages.Rules;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using MailboxEdit = Mailflow.Web.Pages.Mailboxes.EditModel;
using MailboxIndex = Mailflow.Web.Pages.Mailboxes.IndexModel;
using CategoryEdit = Mailflow.Web.Pages.Categories.EditModel;
using RuleEdit = Mailflow.Web.Pages.Rules.EditModel;

namespace Mailflow.Integration.Tests.Pages;

public class PagesTests : IDisposable
{
    private readonly SqliteFixture _fx = new();
    private readonly FakeSecretStore _secrets = new();
    private readonly FakeMailClient _mail = new();
    private readonly Mailflow.Application.Abstractions.IMailboxConnectionFactory _factory = new FakeConnectionFactory(new FakeSecretStore());

    public void Dispose() => _fx.Dispose();

    private static MailboxEdit.MailboxInput MailboxForm(string? password) => new()
    {
        DisplayName = "Compta", Email = "a@b.fr", Host = "imap.b.fr", Port = 993, UseSsl = true,
        Username = "a@b.fr", Password = password, InboxFolder = "INBOX", CheckIntervalMinutes = 5, LookbackDays = 3,
    };

    private async Task<int> CreateMailbox(string password)
    {
        using var db = _fx.NewDb();
        var page = new MailboxEdit(db, _secrets, _mail, _factory).With();
        page.Input = MailboxForm(password);
        Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(null));
        return db.Mailboxes.Single().Id;
    }

    [Fact]
    public async Task Mailbox_create_protects_password_and_requires_it()
    {
        using (var db = _fx.NewDb())
        {
            var page = new MailboxEdit(db, _secrets, _mail, _factory).With();
            page.Input = MailboxForm(null);
            Assert.IsType<PageResult>(await page.OnPostAsync(null));
            Assert.Empty(db.Mailboxes);
        }
        await CreateMailbox("secret");
        using var check = _fx.NewDb();
        Assert.Equal("enc:secret", check.Mailboxes.Single().EncryptedPassword);
    }

    [Fact]
    public async Task Mailbox_without_username_uses_the_email_address()
    {
        using var db = _fx.NewDb();
        var page = new MailboxEdit(db, _secrets, _mail, _factory).With();
        page.Input = MailboxForm("secret");
        page.Input.Username = null;
        Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(null));
        Assert.Equal("a@b.fr", db.Mailboxes.Single().Username);
    }

    [Fact]
    public async Task Mailbox_edit_with_blank_password_keeps_old_one_and_new_password_replaces()
    {
        var id = await CreateMailbox("old");

        using (var db = _fx.NewDb())
        {
            var page = new MailboxEdit(db, _secrets, _mail, _factory).With();
            page.Input = MailboxForm("");
            page.Input.DisplayName = "Renommée";
            Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(id));
        }
        using (var db = _fx.NewDb())
        {
            var m = db.Mailboxes.Single();
            Assert.Equal("Renommée", m.DisplayName);
            Assert.Equal("enc:old", m.EncryptedPassword);
        }
        using (var db = _fx.NewDb())
        {
            var page = new MailboxEdit(db, _secrets, _mail, _factory).With();
            page.Input = MailboxForm("new");
            await page.OnPostAsync(id);
        }
        using var final = _fx.NewDb();
        Assert.Equal("enc:new", final.Mailboxes.Single().EncryptedPassword);
    }

    [Fact]
    public async Task Mailbox_test_connection_uses_stored_password_when_blank_and_never_renders_it()
    {
        var id = await CreateMailbox("stored");
        using var db = _fx.NewDb();
        var page = new MailboxEdit(db, _secrets, _mail, _factory).With();
        page.Input = MailboxForm("");
        var result = Assert.IsType<PartialViewResult>(await page.OnPostTestAsync(id, default));
        Assert.Equal("stored", _mail.Last!.Password);
        var model = Assert.IsType<Mailflow.Application.Abstractions.ConnectionTestResult>(result.Model);
        Assert.Equal(12, model.MessageCount);

        var get = new MailboxEdit(db, _secrets, _mail, _factory).With();
        await get.OnGetAsync(id);
        Assert.Null(get.Input.Password);
    }

    [Fact]
    public async Task Mailbox_delete_blocked_when_messages_were_processed_else_removed()
    {
        var id = await CreateMailbox("p");
        using (var db = _fx.NewDb())
        {
            db.ProcessedMessages.Add(new ProcessedMessage { MailboxId = id, MessageId = "m" });
            db.SaveChanges();
            var page = new MailboxIndex(db).With();
            Assert.IsType<PartialViewResult>(await page.OnPostDeleteAsync(id));
            Assert.Single(db.Mailboxes);
            db.ProcessedMessages.RemoveRange(db.ProcessedMessages);
            db.SaveChanges();
            Assert.IsType<ContentResult>(await page.OnPostDeleteAsync(id));
            Assert.Empty(db.Mailboxes);
        }
    }

    [Theory]
    [InlineData("Factures", true)]
    [InlineData("Fournisseurs/Factures", true)]
    [InlineData("..", false)]
    [InlineData("../x", false)]
    [InlineData("a/../../x", false)]
    [InlineData("a\\..\\x", false)]
    [InlineData("C:\\Windows", false)]
    [InlineData("\\\\srv\\share", false)]
    [InlineData("/etc", false)]
    [InlineData("", false)]
    [InlineData("a/", false)]
    public void Category_path_validation(string path, bool expected) => Assert.Equal(expected, CategoryPath.IsValid(path));

    [Fact]
    public async Task Category_with_traversal_is_rejected_and_valid_one_saved()
    {
        using var db = _fx.NewDb();
        var page = new CategoryEdit(db).With();
        page.Input = new CategoryEdit.CategoryInput { Name = "X", LocalSubfolder = "../evil", Color = "#112233" };
        Assert.IsType<PageResult>(await page.OnPostAsync(null));
        Assert.Empty(db.Categories);

        page.ModelState.Clear();
        page.Input.LocalSubfolder = "Factures";
        Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(null));
        Assert.Equal("Factures", db.Categories.Single().LocalSubfolder);
    }

    [Fact]
    public async Task Rule_save_persists_conditions_and_actions_and_edit_replaces_them()
    {
        int catId, printerId;
        using (var db = _fx.NewDb())
        {
            var c = new Category { Name = "Factures", LocalSubfolder = "Factures" };
            var p = new Printer { DisplayName = "P", WindowsName = "HP" };
            db.Categories.Add(c); db.Printers.Add(p); db.SaveChanges();
            catId = c.Id; printerId = p.Id;
        }

        int ruleId;
        using (var db = _fx.NewDb())
        {
            var page = new RuleEdit(db, null!).With();
            page.Input = new RuleEdit.RuleInput
            {
                Name = "Factures", MatchMode = MatchMode.Any, StopProcessing = true,
                Conditions = [new ConditionInput { Field = ConditionField.Subject, Operator = ConditionOperator.Contains, Value = "facture", CaseSensitive = true },
                              new ConditionInput { Field = ConditionField.HasPdf }],
                Actions = [new ActionInput { Type = ActionType.SaveToCategory, CategoryId = catId },
                           new ActionInput { Type = ActionType.Print, PrinterId = printerId, Copies = 2 }],
            };
            Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(null));
            ruleId = db.Rules.Single().Id;
        }
        using (var db = _fx.NewDb())
        {
            var r = db.Rules.Include(x => x.Conditions).Include(x => x.Actions).Single();
            Assert.Equal(1, r.Priority);
            Assert.True(r.StopProcessing);
            Assert.Equal(2, r.Conditions.Count);
            Assert.Contains(r.Conditions, c => c.Value == "facture" && c.CaseSensitive);
            Assert.Contains(r.Actions, a => a.Type == ActionType.Print && a.PrinterId == printerId && a.Copies == 2);
        }
        using (var db = _fx.NewDb())
        {
            var page = new RuleEdit(db, null!).With();
            page.Input = new RuleEdit.RuleInput
            {
                Name = "Renommée",
                Conditions = [new ConditionInput { Field = ConditionField.FromDomain, Operator = ConditionOperator.Equals, Value = "x.fr" }],
                Actions = [new ActionInput { Type = ActionType.MarkAsRead }],
            };
            Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(ruleId));
        }
        using var final = _fx.NewDb();
        var edited = final.Rules.Include(x => x.Conditions).Include(x => x.Actions).Single();
        Assert.Equal("Renommée", edited.Name);
        Assert.Single(edited.Conditions);
        Assert.Single(edited.Actions);
        Assert.Equal(1, final.RuleConditions.Count());
        Assert.Equal(1, final.RuleActions.Count());
    }

    [Fact]
    public async Task Rule_save_rejects_invalid_regex_and_missing_action_target()
    {
        using var db = _fx.NewDb();
        var page = new RuleEdit(db, null!).With();
        page.Input = new RuleEdit.RuleInput
        {
            Name = "Bad",
            Conditions = [new ConditionInput { Field = ConditionField.Subject, Operator = ConditionOperator.Regex, Value = "(" }],
            Actions = [new ActionInput { Type = ActionType.Print }],
        };
        Assert.IsType<PageResult>(await page.OnPostAsync(null));
        Assert.Empty(db.Rules);
        Assert.True(page.ModelState.ErrorCount >= 2);
    }

    [Fact]
    public async Task Rule_move_swaps_priorities()
    {
        using var db = _fx.NewDb();
        db.Rules.AddRange(new Rule { Name = "A", Priority = 1 }, new Rule { Name = "B", Priority = 2 });
        db.SaveChanges();
        var b = db.Rules.Single(r => r.Name == "B");
        var page = new Mailflow.Web.Pages.Rules.IndexModel(db).With();
        await page.OnPostMoveAsync(b.Id, "up");
        Assert.Equal(1, db.Rules.Single(r => r.Name == "B").Priority);
        Assert.Equal(2, db.Rules.Single(r => r.Name == "A").Priority);
    }

}
