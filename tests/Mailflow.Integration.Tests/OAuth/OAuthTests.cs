using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;
using Mailflow.Infrastructure.Mail;
using Mailflow.Infrastructure.OAuth;
using Mailflow.Integration.Tests.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mailflow.Integration.Tests.OAuth;

internal sealed class FakeMsal : IMsalClient
{
    public Func<Func<DeviceCodeInfo, Task>, CancellationToken, Task<DeviceCodeResult>>? Device { get; set; }
    public Func<string, SilentResult>? Silent { get; set; }
    public int DeviceCalls;
    public string? LastSilentCache;

    public Task<DeviceCodeResult> AcquireByDeviceCodeAsync(string clientId, string? cacheBlob, Func<DeviceCodeInfo, Task> onCode, CancellationToken ct)
    {
        Interlocked.Increment(ref DeviceCalls);
        return Device!(onCode, ct);
    }

    public Task<SilentResult> AcquireSilentAsync(string clientId, string cacheBlob, string? username, CancellationToken ct)
    {
        LastSilentCache = cacheBlob;
        return Task.FromResult(Silent!(cacheBlob));
    }
}

internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<string> Lines { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Lines.Add(formatter(state, exception) + exception?.Message);
}

public sealed class OAuthSignInServiceTests : IDisposable
{
    private const string Secret = "SECRET-TOKEN-VALUE";
    private readonly SqliteFixture _f = new();
    private readonly FakeMsal _msal = new();
    private readonly RecordingLogger<OAuthSignInService> _log = new();
    private readonly OAuthSignInService _svc;

    public OAuthSignInServiceTests()
    {
        using (var db = _f.NewDb())
        {
            db.Stores.Add(new Store { Id = 1, Name = "Store A" });
            db.Mailboxes.Add(new Mailbox { Id = 1, StoreId = 1, Email = "a@example.com", Username = "a@example.com", AuthMethod = AuthMethod.MicrosoftOAuth });
            db.Settings.Add(new Setting { Key = "OAuthMicrosoftClientId", Value = "00000000-0000-0000-0000-000000000000" });
            db.SaveChanges();
        }
        var sc = new ServiceCollection();
        sc.AddScoped<IAppDbContext>(_ => _f.NewDb());
        _svc = new OAuthSignInService(sc.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), _msal, new FakeSecretStore(), _log);
    }

    private static DeviceCodeInfo Code(int seconds = 600) => new("ABCD1234", "https://microsoft.com/devicelogin", DateTime.UtcNow.AddSeconds(seconds));

    private string? StoredCache() { using var db = _f.NewDb(); return db.Mailboxes.Single().EncryptedOAuthCache; }

    private async Task WaitFor(int id, OAuthSignInState state)
    {
        for (var i = 0; i < 200 && _svc.GetStatus(id).State != state; i++) await Task.Delay(10);
        Assert.Equal(state, _svc.GetStatus(id).State);
    }

    [Fact]
    public async Task Start_returns_the_code_then_success_stores_an_encrypted_cache()
    {
        var release = new TaskCompletionSource();
        _msal.Device = async (onCode, ct) =>
        {
            await onCode(Code());
            await release.Task;
            return new DeviceCodeResult(Secret, "CACHE-BLOB", "a@example.com");
        };

        var status = await _svc.StartAsync(1, default);

        Assert.Equal(OAuthSignInState.WaitingForUser, status.State);
        Assert.Equal("ABCD1234", status.Code!.UserCode);
        Assert.Equal(OAuthSignInState.WaitingForUser, _svc.GetStatus(1).State);
        Assert.Null(StoredCache());

        release.SetResult();
        await WaitFor(1, OAuthSignInState.Succeeded);

        Assert.Equal("enc:CACHE-BLOB", StoredCache());
        Assert.DoesNotContain(Secret, StoredCache());
        using var db = _f.NewDb();
        Assert.True(_svc.IsSignedIn(db.Mailboxes.Single()));
        Assert.DoesNotContain(_log.Lines, l => l.Contains(Secret) || l.Contains("ABCD1234"));
    }

    [Theory]
    [InlineData(OAuthFailure.CodeExpired, "expiré")]
    [InlineData(OAuthFailure.Declined, "refusée")]
    [InlineData(OAuthFailure.WrongAccountType, "comptes personnels")]
    [InlineData(OAuthFailure.Network, "internet")]
    public async Task Failures_are_mapped_to_short_french_messages(OAuthFailure kind, string expected)
    {
        _msal.Device = async (onCode, ct) =>
        {
            await onCode(Code());
            throw new OAuthFailureException(kind);
        };

        await _svc.StartAsync(1, default);
        await WaitFor(1, OAuthSignInState.Failed);

        Assert.Contains(expected, _svc.GetStatus(1).Error);
        Assert.Null(StoredCache());
    }

    [Fact]
    public async Task Failure_before_the_code_is_returned_by_Start()
    {
        _msal.Device = (_, _) => throw new OAuthFailureException(OAuthFailure.Network);

        var status = await _svc.StartAsync(1, default);

        Assert.Equal(OAuthSignInState.Failed, status.State);
        Assert.Contains("internet", status.Error);
    }

    [Fact]
    public async Task Unexpected_error_is_logged_without_its_message()
    {
        _msal.Device = (_, _) => throw new InvalidOperationException(Secret);

        var status = await _svc.StartAsync(1, default);

        Assert.Equal(OAuthSignInState.Failed, status.State);
        Assert.DoesNotContain(Secret, status.Error);
        Assert.DoesNotContain(_log.Lines, l => l.Contains(Secret));
    }

    [Fact]
    public async Task Code_expiry_fails_the_sign_in()
    {
        _msal.Device = async (onCode, ct) =>
        {
            await onCode(Code(0));
            await Task.Delay(Timeout.Infinite, ct);
            return null!;
        };

        await _svc.StartAsync(1, default);
        await WaitFor(1, OAuthSignInState.Failed);

        Assert.Contains("expiré", _svc.GetStatus(1).Error);
    }

    [Fact]
    public async Task Not_configured_returns_Failed_without_calling_Microsoft()
    {
        using (var db = _f.NewDb()) { db.Settings.RemoveRange(db.Settings); db.SaveChanges(); }

        var status = await _svc.StartAsync(1, default);

        Assert.False(await _svc.IsConfiguredAsync(default));
        Assert.Equal(OAuthSignInState.Failed, status.State);
        Assert.Contains("non configuré", status.Error);
        Assert.Equal(0, _msal.DeviceCalls);
    }

    [Fact]
    public async Task Restarting_cancels_the_previous_sign_in_and_ignores_its_result()
    {
        var first = true;
        var firstToken = CancellationToken.None;
        _msal.Device = async (onCode, ct) =>
        {
            var isFirst = first; first = false;
            if (isFirst) firstToken = ct;
            await onCode(Code());
            if (isFirst) { await Task.Delay(Timeout.Infinite, ct); }
            return new DeviceCodeResult(Secret, "SECOND", null);
        };

        await _svc.StartAsync(1, default);
        var second = await _svc.StartAsync(1, default);

        Assert.True(firstToken.IsCancellationRequested);
        Assert.Equal(OAuthSignInState.WaitingForUser, second.State);
        await WaitFor(1, OAuthSignInState.Succeeded);
        Assert.Equal("enc:SECOND", StoredCache());
    }

    [Fact]
    public async Task SignOut_clears_the_cache_and_the_pending_state()
    {
        using (var db = _f.NewDb()) { db.Mailboxes.Single().EncryptedOAuthCache = "enc:x"; db.SaveChanges(); }
        _msal.Device = async (onCode, ct) => { await onCode(Code()); await Task.Delay(Timeout.Infinite, ct); return null!; };
        await _svc.StartAsync(1, default);

        await _svc.SignOutAsync(1, default);

        Assert.Null(StoredCache());
        Assert.Equal(OAuthSignInState.None, _svc.GetStatus(1).State);
    }

    public void Dispose() => _f.Dispose();
}

public sealed class MailboxConnectionFactoryTests : IDisposable
{
    private readonly SqliteFixture _f = new();
    private readonly FakeMsal _msal = new();

    private MailboxConnectionFactory Factory(TestDb db) => new(db, new FakeSecretStore(), _msal);

    private TestDb Seed(AuthMethod method, string? cache, bool clientId = true)
    {
        var db = _f.NewDb();
        db.Stores.Add(new Store { Id = 1, Name = "Store A" });
        db.Mailboxes.Add(new Mailbox
        {
            Id = 1, StoreId = 1, Email = "a@example.com", Username = "a@example.com", Host = "imap.example.com",
            AuthMethod = method, EncryptedPassword = "enc:pw", EncryptedOAuthCache = cache,
        });
        if (clientId) db.Settings.Add(new Setting { Key = "OAuthMicrosoftClientId", Value = "00000000-0000-0000-0000-000000000000" });
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task Password_path_decrypts_the_password_and_carries_no_token()
    {
        using var db = Seed(AuthMethod.Password, null, clientId: false);

        var c = await Factory(db).CreateAsync(db.Mailboxes.Single(), default);

        Assert.Equal("pw", c.Password);
        Assert.Null(c.AccessToken);
        Assert.Equal("imap.example.com", c.Host);
    }

    [Fact]
    public async Task OAuth_silent_path_returns_the_token_and_persists_the_refreshed_cache()
    {
        using var db = Seed(AuthMethod.MicrosoftOAuth, "enc:OLD");
        _msal.Silent = cache => new SilentResult("ACCESS", "NEW");

        var c = await Factory(db).CreateAsync(db.Mailboxes.Single(), default);

        Assert.Equal("OLD", _msal.LastSilentCache);
        Assert.Equal("ACCESS", c.AccessToken);
        Assert.Equal("", c.Password);
        Assert.Equal("a@example.com", c.Username);
        using var check = _f.NewDb();
        Assert.Equal("enc:NEW", check.Mailboxes.Single().EncryptedOAuthCache);
    }

    [Fact]
    public async Task Missing_cache_requires_reconnection()
    {
        using var db = Seed(AuthMethod.MicrosoftOAuth, null);
        var ex = await Assert.ThrowsAsync<OAuthReconnectRequiredException>(() => Factory(db).CreateAsync(db.Mailboxes.Single(), default));
        Assert.Contains("Reconnexion Microsoft requise", ex.Message);
    }

    [Fact]
    public async Task Missing_client_id_requires_reconnection()
    {
        using var db = Seed(AuthMethod.MicrosoftOAuth, "enc:OLD", clientId: false);
        await Assert.ThrowsAsync<OAuthReconnectRequiredException>(() => Factory(db).CreateAsync(db.Mailboxes.Single(), default));
    }

    [Fact]
    public async Task Interaction_needed_requires_reconnection()
    {
        using var db = Seed(AuthMethod.MicrosoftOAuth, "enc:OLD");
        _msal.Silent = _ => throw new OAuthInteractionRequiredException();
        await Assert.ThrowsAsync<OAuthReconnectRequiredException>(() => Factory(db).CreateAsync(db.Mailboxes.Single(), default));
    }

    public void Dispose() => _f.Dispose();
}

public class MailKitOAuthTests
{
    [Fact]
    public void Token_selects_XOAUTH2_and_no_token_selects_the_password()
    {
        var withToken = MailKitClient.OAuthMechanismFor(new MailboxConnection("h", 993, true, "a@example.com", "", "tok"));
        Assert.Equal("XOAUTH2", withToken!.MechanismName);
        Assert.Null(MailKitClient.OAuthMechanismFor(new MailboxConnection("h", 993, true, "a@example.com", "pw")));
    }

    [Fact]
    public void Token_connection_keeps_the_TLS_policy()
    {
        var c = new MailboxConnection("imap.example.com", 143, false, "a@example.com", "", "tok");
        Assert.Equal(MailKit.Security.SecureSocketOptions.StartTls, MailKitClient.SocketOptionsFor(c));
    }

    [Fact]
    public void Token_is_not_echoed_by_the_connection_description_of_the_mechanism_name()
    {
        var m = MailKitClient.OAuthMechanismFor(new MailboxConnection("h", 993, true, "u", "", "TOP-SECRET"))!;
        Assert.DoesNotContain("TOP-SECRET", m.ToString() + m.MechanismName);
    }
}
