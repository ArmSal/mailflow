using Mailflow.Application.Abstractions;
using Mailflow.Web.Auth;
using Mailflow.Web.Backup;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SettingsIndex = Mailflow.Web.Pages.Settings.IndexModel;

namespace Mailflow.Integration.Tests.Pages;

public sealed class DocumentsRootSettingsTests : IDisposable
{
    private readonly SqliteFixture _fx = new();
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "mf-root-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { _fx.Dispose(); if (Directory.Exists(_tmp)) Directory.Delete(_tmp, true); }

    private static SettingsIndex NewSettings(TestDb db)
    {
        var backup = new BackupService(Substitute.For<IServiceScopeFactory>(), new BackupOptions("", ""), NullLogger<BackupService>.Instance);
        var page = new SettingsIndex(db, Substitute.For<IAdminPasswordStore>(), Substitute.For<IPasswordHasher<object>>(), backup, Options.Create(new Mailflow.Infrastructure.Storage.DocumentStoreOptions())).With();
        page.PageContext.HttpContext = new DefaultHttpContext();
        return page;
    }

    [Fact]
    public async Task Saving_is_not_blocked_by_the_other_forms_on_the_page()
    {
        using var db = _fx.NewDb();
        var root = Path.Combine(_tmp, "docs-other-forms");
        var page = NewSettings(db);
        // A browser posts only the Settings form, so model binding finds the password form's [Required] fields empty
        // and the sender-check mode missing. Those errors belong to other forms and must not stop this save.
        // The keys are the ones ASP.NET really produces (bare names, found by inspecting a real post), not "Password.Current".
        page.ModelState.AddModelError("Current", "required");
        page.ModelState.AddModelError("New", "required");
        page.ModelState.AddModelError("Confirm", "required");
        page.ModelState.AddModelError("Password.Current", "required");
        page.ModelState.AddModelError("SenderCheck.Mode", "invalid");
        page.Input = new SettingsIndex.SettingsInput { DocumentsRoot = root, FileNamePattern = "{Category}/{OriginalName}.pdf" };

        await page.OnPostSettingsAsync();

        Assert.NotNull(page.SettingsMessage);
        using var check = _fx.NewDb();
        Assert.Equal(root, check.Settings.Single(s => s.Key == "DocumentsRoot").Value);
    }

    [Fact]
    public async Task A_real_error_in_the_settings_fields_still_blocks_the_save()
    {
        using var db = _fx.NewDb();
        var page = NewSettings(db);
        page.Input = new SettingsIndex.SettingsInput { DocumentsRoot = "relative\\path", FileNamePattern = "{Category}/{OriginalName}.pdf" };

        await page.OnPostSettingsAsync();

        Assert.Null(page.SettingsMessage);
        Assert.False(page.ModelState.IsValid);
        using var check = _fx.NewDb();
        Assert.Empty(check.Settings.Where(s => s.Key == "DocumentsRoot"));
    }

    [Fact]
    public async Task Chosen_documents_folder_and_pattern_are_saved_and_shown_again()
    {
        using var db = _fx.NewDb();
        var root = Path.Combine(_tmp, "docs");
        var page = NewSettings(db);
        page.Input = new SettingsIndex.SettingsInput { DocumentsRoot = root, FileNamePattern = "{Category}/{dd-MM-yyyy}_{OriginalName}.pdf" };
        await page.OnPostSettingsAsync();

        Assert.True(page.ModelState.IsValid);
        using var db2 = _fx.NewDb();
        Assert.Equal(root, db2.Settings.Single(s => s.Key == "DocumentsRoot").Value);
        var reopened = NewSettings(db2);
        await reopened.OnGetAsync();
        Assert.Equal(root, reopened.Input.DocumentsRoot);
    }
}
