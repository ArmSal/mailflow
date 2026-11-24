using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;
using Mailflow.Web.Auth;
using Mailflow.Web.Backup;
using Mailflow.Web.Pages.Mailboxes;
using Mailflow.Web.Pages.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using MailboxEdit = Mailflow.Web.Pages.Mailboxes.EditModel;
using SettingsIndex = Mailflow.Web.Pages.Settings.IndexModel;

namespace Mailflow.Integration.Tests.Pages;

public class OAuthPagesTests : IDisposable
{
    private readonly SqliteFixture _fx = new();
    private readonly FakeSecretStore _secrets = new();
    private readonly FakeMailClient _mail = new();
    private readonly IOAuthSignInService _oauth = Substitute.For<IOAuthSignInService>();
    private readonly IMailboxConnectionFactory _factory = Substitute.For<IMailboxConnectionFactory>();

    public OAuthPagesTests() => _oauth.GetStatus(default).ReturnsForAnyArgs(new OAuthSignInStatus(OAuthSignInState.None));

    public void Dispose() => _fx.Dispose();

    private MailboxEdit NewPage(TestDb db) => new MailboxEdit(db, _secrets, _mail, _oauth, _factory).With();

    private int SeedStore()
    {
        using var db = _fx.NewDb();
        var s = new Store { Name = "Store A" };
        db.Stores.Add(s);
        db.SaveChanges();
        return s.Id;
    }

    private static MailboxEdit.MailboxInput Form(int storeId, AuthMethod method, string? password = null) => new()
    {
        StoreId = storeId, AuthMethod = method, DisplayName = "Box", Email = "user@example.com", Host = "outlook.office365.com", Port = 993,
        UseSsl = true, Username = "user@example.com", Password = password, InboxFolder = "INBOX", CheckIntervalMinutes = 5, LookbackDays = 3,
    };

    private int SeedMsMailbox()
    {
        var storeId = SeedStore();
        using var db = _fx.NewDb();
        var m = new Mailbox { StoreId = storeId, AuthMethod = AuthMethod.MicrosoftOAuth, DisplayName = "Box", Email = "user@example.com", Host = "h", Username = "user@example.com", EncryptedPassword = "" };
        db.Mailboxes.Add(m);
        db.SaveChanges();
        return m.Id;
    }

    [Fact]
    public async Task Oauth_mailbox_saves_without_password()
    {
        var storeId = SeedStore();
        using var db = _fx.NewDb();
        var page = NewPage(db);
        page.Input = Form(storeId, AuthMethod.MicrosoftOAuth, "ignored");
        Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(null));
        var m = db.Mailboxes.Single();
        Assert.Equal(AuthMethod.MicrosoftOAuth, m.AuthMethod);
        Assert.Equal("", m.EncryptedPassword);
    }

    [Fact]
    public async Task Oauth_mailbox_still_requires_a_username()
    {
        var storeId = SeedStore();
        using var db = _fx.NewDb();
        var page = NewPage(db);
        page.Input = Form(storeId, AuthMethod.MicrosoftOAuth);
        page.Input.Username = " ";
        Assert.IsType<PageResult>(await page.OnPostAsync(null));
        Assert.Empty(db.Mailboxes);
    }

    [Fact]
    public async Task Password_mailbox_still_requires_a_password_on_create()
    {
        var storeId = SeedStore();
        using var db = _fx.NewDb();
        var page = NewPage(db);
        page.Input = Form(storeId, AuthMethod.Password);
        Assert.IsType<PageResult>(await page.OnPostAsync(null));
        Assert.Empty(db.Mailboxes);
    }

    [Fact]
    public async Task Switching_an_oauth_mailbox_to_password_requires_one_and_blank_then_keeps_it()
    {
        var id = SeedMsMailbox();
        int storeId;
        using (var db = _fx.NewDb()) storeId = db.Stores.Single().Id;
        using (var db = _fx.NewDb())
        {
            var page = NewPage(db);
            page.Input = Form(storeId, AuthMethod.Password);
            Assert.IsType<PageResult>(await page.OnPostAsync(id));
        }
        using (var db = _fx.NewDb())
        {
            var page = NewPage(db);
            page.Input = Form(storeId, AuthMethod.Password, "pw");
            Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(id));
        }
        using (var db = _fx.NewDb())
        {
            var page = NewPage(db);
            page.Input = Form(storeId, AuthMethod.Password, "");
            Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(id));
            Assert.Equal("enc:pw", db.Mailboxes.Single().EncryptedPassword);
        }
    }

    [Fact]
    public async Task Start_returns_the_waiting_panel_with_the_code()
    {
        var id = SeedMsMailbox();
        _oauth.IsConfiguredAsync(default).ReturnsForAnyArgs(true);
        var code = new DeviceCodeInfo("ABCD1234", "https://example.com/devicelogin", DateTime.UtcNow.AddMinutes(15));
        _oauth.StartAsync(id, default).ReturnsForAnyArgs(new OAuthSignInStatus(OAuthSignInState.WaitingForUser, code));
        using var db = _fx.NewDb();
        var result = Assert.IsType<PartialViewResult>(await NewPage(db).OnPostMsStartAsync(id, default));
        Assert.Equal("_MsSignIn", result.ViewName);
        var panel = Assert.IsType<MsSignInPanel>(result.Model);
        Assert.Equal("ABCD1234", panel.Status.Code!.UserCode);
        Assert.False(panel.SignedIn);
    }

    [Fact]
    public async Task Start_without_configured_client_id_fails_without_calling_the_service()
    {
        var id = SeedMsMailbox();
        _oauth.IsConfiguredAsync(default).ReturnsForAnyArgs(false);
        using var db = _fx.NewDb();
        var panel = Assert.IsType<MsSignInPanel>(Assert.IsType<PartialViewResult>(await NewPage(db).OnPostMsStartAsync(id, default)).Model);
        Assert.Equal(OAuthSignInState.Failed, panel.Status.State);
        await _oauth.DidNotReceiveWithAnyArgs().StartAsync(default, default);
    }

    [Fact]
    public async Task Status_follows_waiting_succeeded_failed()
    {
        var id = SeedMsMailbox();
        var code = new DeviceCodeInfo("ABCD1234", "https://example.com/devicelogin", DateTime.UtcNow.AddMinutes(15));
        _oauth.GetStatus(id).Returns(new OAuthSignInStatus(OAuthSignInState.WaitingForUser, code), new OAuthSignInStatus(OAuthSignInState.Succeeded), new OAuthSignInStatus(OAuthSignInState.Failed, null, "Refusé"));
        using var db = _fx.NewDb();
        var page = NewPage(db);
        async Task<MsSignInPanel> Poll() => Assert.IsType<MsSignInPanel>(Assert.IsType<PartialViewResult>(await page.OnGetMsStatusAsync(id, default)).Model);
        Assert.Equal(OAuthSignInState.WaitingForUser, (await Poll()).Status.State);
        var done = await Poll();
        Assert.Equal(OAuthSignInState.Succeeded, done.Status.State);
        Assert.True(done.SignedIn);
        var failed = await Poll();
        Assert.Equal("Refusé", failed.Status.Error);
        Assert.False(failed.SignedIn);
    }

    [Fact]
    public async Task Sign_out_calls_the_service_and_shows_not_connected()
    {
        var id = SeedMsMailbox();
        using var db = _fx.NewDb();
        var panel = Assert.IsType<MsSignInPanel>(Assert.IsType<PartialViewResult>(await NewPage(db).OnPostMsSignOutAsync(id, default)).Model);
        await _oauth.Received(1).SignOutAsync(id, Arg.Any<CancellationToken>());
        Assert.False(panel.SignedIn);
    }

    [Fact]
    public async Task Test_builds_the_connection_through_the_factory_for_oauth_mailboxes()
    {
        var id = SeedMsMailbox();
        _factory.CreateAsync(default!, default).ReturnsForAnyArgs(new MailboxConnection("h", 993, true, "user@example.com", "", "token"));
        using var db = _fx.NewDb();
        var page = NewPage(db);
        page.Input = Form(db.Stores.Single().Id, AuthMethod.MicrosoftOAuth);
        var result = Assert.IsType<PartialViewResult>(await page.OnPostTestAsync(id, default));
        Assert.True(Assert.IsType<ConnectionTestResult>(result.Model).Success);
        Assert.Equal("token", _mail.Last!.AccessToken);
        await _factory.Received(1).CreateAsync(Arg.Is<Mailbox>(m => m.AuthMethod == AuthMethod.MicrosoftOAuth && m.Id == id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Test_maps_reconnect_required_to_a_failed_result_with_its_message()
    {
        var id = SeedMsMailbox();
        _factory.CreateAsync(default!, default).ReturnsForAnyArgs<Task<MailboxConnection>>(_ => throw new OAuthReconnectRequiredException("Reconnectez la boîte."));
        using var db = _fx.NewDb();
        var page = NewPage(db);
        page.Input = Form(db.Stores.Single().Id, AuthMethod.MicrosoftOAuth);
        var model = Assert.IsType<ConnectionTestResult>(Assert.IsType<PartialViewResult>(await page.OnPostTestAsync(id, default)).Model);
        Assert.False(model.Success);
        Assert.Equal("Reconnectez la boîte.", model.Error);
    }

    [Fact]
    public async Task Test_on_an_unsaved_oauth_mailbox_asks_to_save_first()
    {
        using var db = _fx.NewDb();
        var page = NewPage(db);
        page.Input = Form(SeedStore(), AuthMethod.MicrosoftOAuth);
        var model = Assert.IsType<ConnectionTestResult>(Assert.IsType<PartialViewResult>(await page.OnPostTestAsync(null, default)).Model);
        Assert.False(model.Success);
        Assert.Contains("Enregistrez d'abord", model.Error);
    }

    [Theory]
    [InlineData("The LOGIN command is disabled")]
    [InlineData("AUTHENTICATE is disabled by policy")]
    [InlineData("Basic authentication is disabled")]
    public async Task Test_translates_password_refused_errors_to_a_french_hint(string raw)
    {
        var id = SeedMsMailbox();
        _factory.CreateAsync(default!, default).ReturnsForAnyArgs<Task<MailboxConnection>>(_ => throw new InvalidOperationException(raw));
        using var db = _fx.NewDb();
        var page = NewPage(db);
        page.Input = Form(db.Stores.Single().Id, AuthMethod.MicrosoftOAuth);
        var result = Assert.IsType<PartialViewResult>(await page.OnPostTestAsync(id, default));
        Assert.Equal(raw, Assert.IsType<ConnectionTestResult>(result.Model).Error);
        Assert.Contains("Compte Microsoft (OAuth2)", Assert.IsType<string>(result.ViewData["Hint"]));
    }

    [Fact]
    public void Unrelated_errors_get_no_hint() => Assert.Null(MailErrorHints.For("Connection refused"));

    private SettingsIndex NewSettings(TestDb db)
    {
        var scopes = Substitute.For<IServiceScopeFactory>();
        var backup = new BackupService(scopes, new BackupOptions("", ""), NullLogger<BackupService>.Instance);
        var page = new SettingsIndex(db, Substitute.For<IAdminPasswordStore>(), Substitute.For<IPasswordHasher<object>>(), backup, Options.Create(new Mailflow.Infrastructure.Storage.DocumentStoreOptions())).With();
        page.PageContext.HttpContext = new DefaultHttpContext();
        return page;
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000", true)]
    [InlineData("  00000000-0000-0000-0000-000000000001  ", true)]
    [InlineData("", true)]
    [InlineData("not-a-guid", false)]
    public async Task Settings_client_id_accepts_guid_or_empty_only(string value, bool valid)
    {
        using var db = _fx.NewDb();
        var page = NewSettings(db);
        page.Microsoft = new SettingsIndex.MicrosoftInput { ClientId = value };
        await page.OnPostMicrosoftAsync();
        Assert.Equal(valid, page.ModelState.IsValid);
        var stored = db.Settings.SingleOrDefault(s => s.Key == "OAuthMicrosoftClientId")?.Value;
        if (valid) Assert.Equal(value.Trim(), stored ?? "");
        else Assert.Null(stored);
        Assert.Equal(valid && value.Trim().Length > 0, page.MicrosoftConfigured);
    }
}
