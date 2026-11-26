using Mailflow.Web.Auth;
using Mailflow.Web.Backup;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Mailflow.Application.Abstractions;
using SettingsIndex = Mailflow.Web.Pages.Settings.IndexModel;

namespace Mailflow.Integration.Tests.Pages;

public sealed class ToastSettingTests : IDisposable
{
    private readonly SqliteFixture _fx = new();
    public void Dispose() => _fx.Dispose();

    private static SettingsIndex NewSettings(TestDb db)
    {
        var backup = new BackupService(Substitute.For<IServiceScopeFactory>(), new BackupOptions("", ""), NullLogger<BackupService>.Instance);
        var page = new SettingsIndex(db, Substitute.For<IAdminPasswordStore>(), Substitute.For<IPasswordHasher<object>>(), backup, Options.Create(new Mailflow.Infrastructure.Storage.DocumentStoreOptions())).With();
        page.PageContext.HttpContext = new DefaultHttpContext();
        return page;
    }

    [Fact]
    public async Task Toasts_are_on_by_default_and_the_switch_is_saved()
    {
        using var db = _fx.NewDb();
        var page = NewSettings(db);
        await page.OnGetAsync();
        Assert.True(page.Input.ToastNotifications);

        var root = Path.Combine(Path.GetTempPath(), "mailflow-toast-" + Guid.NewGuid().ToString("N"));
        try
        {
            page.Input = new SettingsIndex.SettingsInput { DocumentsRoot = root, FileNamePattern = "{OriginalName}", ToastNotifications = false };
            await page.OnPostSettingsAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }

        using var db2 = _fx.NewDb();
        var again = NewSettings(db2);
        await again.OnGetAsync();
        Assert.False(again.Input.ToastNotifications);
    }
}
