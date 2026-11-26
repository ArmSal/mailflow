using Mailflow.Domain.Entities;
using Mailflow.Infrastructure.Storage;
using Mailflow.Integration.Tests.Pages;
using Mailflow.Web.Backup;
using Mailflow.Web.Pages.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Mailflow.Integration.Tests.Settings;

public sealed class SettingsRulesTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "mailflow-set-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(_tmp)) Directory.Delete(_tmp, true); }

    [Theory]
    [InlineData("")]
    [InlineData("relative\\docs")]
    [InlineData("C:\\a\\..\\b")]
    public void Root_rejects_invalid(string root) => Assert.NotNull(SettingsRules.ValidateRoot(root));

    [Fact]
    public void Root_creates_missing_folder_and_accepts()
    {
        var root = Path.Combine(_tmp, "new", "docs");
        Assert.Null(SettingsRules.ValidateRoot(root));
        Assert.True(Directory.Exists(root));
        Assert.Empty(Directory.GetFiles(root));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{Category}/file.pdf")]
    [InlineData("../{OriginalName}.pdf")]
    [InlineData("{Category}/../{OriginalName}.pdf")]
    [InlineData("C:/x/{OriginalName}.pdf")]
    [InlineData("/x/{OriginalName}.pdf")]
    public void Pattern_rejects_invalid(string p) => Assert.NotNull(SettingsRules.ValidatePattern(p));

    [Fact] public void Pattern_accepts_default() => Assert.Null(SettingsRules.ValidatePattern("{Category}/{yyyy}/{OriginalName}.pdf"));

    [Fact]
    public async Task Store_uses_settings_table_then_falls_back_to_options()
    {
        using var fx = new SqliteFixture();
        var rootA = Path.Combine(_tmp, "a");
        var rootB = Path.Combine(_tmp, "b");
        var sp = new ServiceCollection().AddScoped<Mailflow.Application.Abstractions.IAppDbContext>(_ => fx.NewDb()).BuildServiceProvider();
        var store = new FileSystemDocumentStore(Options.Create(new DocumentStoreOptions { DocumentsRoot = rootA }), sp.GetRequiredService<IServiceScopeFactory>());

        var first = await store.SaveAsync([1], "Cat", "S", "x.pdf", new DateTime(2026, 1, 2), default);
        Assert.StartsWith(Path.GetFullPath(rootA), first.Path);

        using (var db = fx.NewDb())
        {
            db.Settings.Add(new Setting { Key = "DocumentsRoot", Value = rootB });
            db.Settings.Add(new Setting { Key = "FileNamePattern", Value = "flat/{OriginalName}.pdf" });
            db.SaveChanges();
        }
        var second = await store.SaveAsync([2], "Cat", "S", "y.pdf", new DateTime(2026, 1, 2), default);
        Assert.Equal(Path.Combine(Path.GetFullPath(rootB), "flat", "y.pdf"), second.Path);
    }

    private static string DbFile(string dir, out string cs)
    {
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "src.db");
        cs = $"Data Source={file};Pooling=False";
        using var c = new SqliteConnection(cs);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "CREATE TABLE Settings(Key TEXT PRIMARY KEY, Value TEXT); INSERT INTO Settings VALUES('DocumentsRoot','x');";
        cmd.ExecuteNonQuery();
        return file;
    }

    private BackupService NewBackup(string root, string cs, SqliteFixture fx)
    {
        var sp = new ServiceCollection().AddScoped<Mailflow.Application.Abstractions.IAppDbContext>(_ => fx.NewDb()).BuildServiceProvider();
        return new BackupService(sp.GetRequiredService<IServiceScopeFactory>(), new BackupOptions(cs, root), NullLogger<BackupService>.Instance);
    }

    [Fact]
    public async Task Backup_creates_valid_sqlite_with_tables_inside_root()
    {
        using var fx = new SqliteFixture();
        DbFile(Path.Combine(_tmp, "data"), out var cs);
        var root = Path.Combine(_tmp, "docs");
        var r = await NewBackup(root, cs, fx).RunAsync(default, new DateTime(2026, 3, 1, 2, 0, 0));

        Assert.True(r.Ok, r.Error);
        Assert.Equal("mailflow-20260301-0200.db", r.FileName);
        var path = Path.Combine(root, "backups", r.FileName!);
        using var c = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Settings WHERE Key='DocumentsRoot'";
        Assert.Equal("x", cmd.ExecuteScalar());
    }

    [Fact]
    public async Task Backup_keeps_only_14_newest()
    {
        using var fx = new SqliteFixture();
        DbFile(Path.Combine(_tmp, "data"), out var cs);
        var root = Path.Combine(_tmp, "docs");
        var svc = NewBackup(root, cs, fx);
        for (var i = 0; i < 16; i++) Assert.True((await svc.RunAsync(default, new DateTime(2026, 3, 1).AddDays(i))).Ok);

        var files = Directory.GetFiles(Path.Combine(root, "backups")).Select(Path.GetFileName).OrderBy(x => x).ToList();
        Assert.Equal(14, files.Count);
        Assert.Equal("mailflow-20260303-0000.db", files[0]);
    }

    [Theory]
    [InlineData("..\\evil.db")]
    [InlineData("..\\..\\evil.db")]
    [InlineData("C:\\evil.db")]
    public void Backup_path_outside_root_rejected(string name)
        => Assert.Throws<InvalidOperationException>(() => BackupService.BackupPath(Path.Combine(_tmp, "docs"), name));

    [Fact]
    public async Task Backup_failure_returns_error_without_throwing()
    {
        using var fx = new SqliteFixture();
        var r = await NewBackup(Path.Combine(_tmp, "docs"), $"Data Source={Path.Combine(_tmp, "missing", "x.db")};Mode=ReadOnly;Pooling=False", fx).RunAsync(default);
        Assert.False(r.Ok);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void Nightly_next_run_is_02_00()
    {
        Assert.Equal(TimeSpan.FromHours(1), NightlyBackupService.UntilNext(new DateTime(2026, 1, 1, 1, 0, 0)));
        Assert.Equal(TimeSpan.FromHours(23), NightlyBackupService.UntilNext(new DateTime(2026, 1, 1, 3, 0, 0)));
    }
}

public sealed class PasswordChangeTests
{
    private sealed class Fixture
    {
        public readonly SqliteFixture Db = new();
        public readonly PasswordHasher<object> Hasher = new();
        public readonly Mailflow.Web.Auth.InMemoryAdminPasswordStore Store = new();
        public Fixture() => Store.SetHashAsync(Hasher.HashPassword(new object(), "oldpassword"), default).Wait();

        public Mailflow.Web.Pages.Settings.IndexModel Page(string cur, string nw, string conf)
        {
            var page = new Mailflow.Web.Pages.Settings.IndexModel(Db.NewDb(), Store, Hasher, null!, Options.Create(new DocumentStoreOptions())).With();
            page.Password = new() { Current = cur, New = nw, Confirm = conf };
            page.PageContext.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            return page;
        }
    }

    [Theory]
    [InlineData("wrong", "newpassword", "newpassword", "incorrect")]
    [InlineData("oldpassword", "short", "short", "8 caract")]
    [InlineData("oldpassword", "newpassword", "different1", "correspondent")]
    public async Task Rejected_cases_keep_old_hash(string cur, string nw, string conf, string expected)
    {
        var f = new Fixture();
        var before = await f.Store.GetHashAsync(default);
        var p = f.Page(cur, nw, conf);
        await p.OnPostPasswordAsync();
        Assert.False(p.PasswordOk);
        Assert.Contains(expected, p.PasswordMessage);
        Assert.Equal(before, await f.Store.GetHashAsync(default));
    }

    [Fact]
    public async Task Success_stores_new_verifiable_hash()
    {
        var f = new Fixture();
        var p = f.Page("oldpassword", "newpassword", "newpassword");
        await p.OnPostPasswordAsync();
        Assert.True(p.PasswordOk);
        var hash = (await f.Store.GetHashAsync(default))!;
        Assert.NotEqual(PasswordVerificationResult.Failed, f.Hasher.VerifyHashedPassword(new object(), hash, "newpassword"));
        Assert.Equal(PasswordVerificationResult.Failed, f.Hasher.VerifyHashedPassword(new object(), hash, "oldpassword"));
    }
}
