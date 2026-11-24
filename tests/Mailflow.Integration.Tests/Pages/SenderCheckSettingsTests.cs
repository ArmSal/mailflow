using Mailflow.Application.Abstractions;
using Mailflow.Web.Auth;
using Mailflow.Web.Backup;
using Mailflow.Web.Pages.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SettingsIndex = Mailflow.Web.Pages.Settings.IndexModel;

namespace Mailflow.Integration.Tests.Pages;

public sealed class SenderCheckSettingsTests : IDisposable
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
    public async Task Missing_setting_displays_Warn()
    {
        using var db = _fx.NewDb();
        var page = NewSettings(db);
        await page.OnGetAsync();
        Assert.Equal("Warn", page.SenderCheck.Mode);
    }

    [Theory]
    [InlineData("Strict", "Strict")]
    [InlineData("Off", "Off")]
    [InlineData("nonsense", "Warn")]
    [InlineData("7", "Warn")]
    [InlineData("", "Warn")]
    public async Task Stored_value_is_displayed_and_unknown_means_Warn(string stored, string shown)
    {
        using var db = _fx.NewDb();
        db.Settings.Add(new Mailflow.Domain.Entities.Setting { Key = "PrintSenderCheck", Value = stored });
        db.SaveChanges();
        var page = NewSettings(db);
        await page.OnGetAsync();
        Assert.Equal(shown, page.SenderCheck.Mode);
    }

    [Theory]
    [InlineData("Off")]
    [InlineData("Warn")]
    [InlineData(" Strict ")]
    public async Task Valid_mode_is_saved_by_enum_name(string mode)
    {
        using var db = _fx.NewDb();
        var page = NewSettings(db);
        page.SenderCheck = new SettingsIndex.SenderCheckInput { Mode = mode };
        await page.OnPostSenderCheckAsync();
        Assert.True(page.ModelState.IsValid);
        Assert.NotNull(page.SenderCheckMessage);
        Assert.Equal(mode.Trim(), db.Settings.Single(s => s.Key == "PrintSenderCheck").Value);
        Assert.Equal(mode.Trim(), page.SenderCheck.Mode);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Paranoid")]
    [InlineData("1")]
    public async Task Invalid_mode_is_rejected_and_not_saved(string? mode)
    {
        using var db = _fx.NewDb();
        var page = NewSettings(db);
        page.SenderCheck = new SettingsIndex.SenderCheckInput { Mode = mode };
        await page.OnPostSenderCheckAsync();
        Assert.False(page.ModelState.IsValid);
        Assert.Null(page.SenderCheckMessage);
        Assert.Empty(db.Settings.Where(s => s.Key == "PrintSenderCheck"));
    }
}
