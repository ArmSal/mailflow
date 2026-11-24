using System.Collections.Concurrent;
using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mailflow.Infrastructure.OAuth;

internal sealed class OAuthSignInService(IServiceScopeFactory scopes, IMsalClient msal, ISecretStore secrets, ILogger<OAuthSignInService> log)
    : IOAuthSignInService
{
    public const string NotConfiguredMessage = "Identifiant d'application Microsoft non configuré : renseignez-le dans Paramètres.";
    private static readonly TimeSpan CodeTimeout = TimeSpan.FromSeconds(30);

    private sealed class Pending
    {
        public readonly CancellationTokenSource Cts = new();
        public readonly TaskCompletionSource<OAuthSignInStatus> FirstStatus = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public volatile OAuthSignInStatus Status = new(OAuthSignInState.None);
        public volatile bool Replaced;
    }

    private readonly ConcurrentDictionary<int, Pending> _pending = new();

    public async Task<bool> IsConfiguredAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        return await OAuthSettings.GetClientIdAsync(scope.ServiceProvider.GetRequiredService<IAppDbContext>(), ct) is not null;
    }

    public async Task<OAuthSignInStatus> StartAsync(int mailboxId, CancellationToken ct)
    {
        string? clientId;
        using (var scope = scopes.CreateScope())
            clientId = await OAuthSettings.GetClientIdAsync(scope.ServiceProvider.GetRequiredService<IAppDbContext>(), ct);
        if (clientId is null) return new OAuthSignInStatus(OAuthSignInState.Failed, Error: NotConfiguredMessage);

        var pending = new Pending();
        if (_pending.TryGetValue(mailboxId, out var old)) Cancel(old);
        _pending[mailboxId] = pending;
        _ = Task.Run(() => RunAsync(mailboxId, clientId, pending));

        var first = pending.FirstStatus.Task;
        if (await Task.WhenAny(first, Task.Delay(CodeTimeout, ct)) != first)
        {
            Cancel(pending);
            return Set(pending, new OAuthSignInStatus(OAuthSignInState.Failed, Error: "Microsoft ne répond pas : réessayez dans un instant."));
        }
        return await first;
    }

    private async Task RunAsync(int mailboxId, string clientId, Pending p)
    {
        try
        {
            var r = await msal.AcquireByDeviceCodeAsync(clientId, null, info =>
            {
                var left = info.ExpiresAtUtc - DateTime.UtcNow;
                p.Cts.CancelAfter(left > TimeSpan.Zero ? left : TimeSpan.Zero);
                p.FirstStatus.TrySetResult(Set(p, new OAuthSignInStatus(OAuthSignInState.WaitingForUser, info)));
                return Task.CompletedTask;
            }, p.Cts.Token);

            if (p.Replaced) return;
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var mailbox = await db.Mailboxes.FirstOrDefaultAsync(m => m.Id == mailboxId, CancellationToken.None);
            if (mailbox is null) { Fail(p, "Boîte mail introuvable."); return; }
            mailbox.EncryptedOAuthCache = secrets.Protect(r.NewCacheBlob);
            await db.SaveChangesAsync(CancellationToken.None);
            Complete(p, new OAuthSignInStatus(OAuthSignInState.Succeeded));
        }
        catch (OperationCanceledException)
        {
            if (!p.Replaced) Fail(p, MessageFor(OAuthFailure.CodeExpired));
        }
        catch (OAuthFailureException ex)
        {
            if (!p.Replaced) Fail(p, MessageFor(ex.Kind));
        }
        catch (Exception ex)
        {
            log.LogWarning("Microsoft sign-in failed ({ErrorType}).", ex.GetType().Name);
            if (!p.Replaced) Fail(p, MessageFor(OAuthFailure.Other));
        }
    }

    private static string MessageFor(OAuthFailure kind) => kind switch
    {
        OAuthFailure.CodeExpired => "Le code a expiré : relancez la connexion.",
        OAuthFailure.Declined => "Connexion refusée sur le compte Microsoft.",
        OAuthFailure.WrongAccountType => "Application non autorisée pour les comptes personnels Microsoft : vérifiez son inscription (types de comptes pris en charge) dans Azure.",
        OAuthFailure.Network => "Impossible de joindre Microsoft : vérifiez la connexion internet.",
        _ => "La connexion Microsoft a échoué."
    };

    private static void Fail(Pending p, string error) => Complete(p, new OAuthSignInStatus(OAuthSignInState.Failed, Error: error));

    private static void Complete(Pending p, OAuthSignInStatus s) => p.FirstStatus.TrySetResult(Set(p, s));

    private static OAuthSignInStatus Set(Pending p, OAuthSignInStatus s) { p.Status = s; return s; }

    private static void Cancel(Pending p)
    {
        p.Replaced = true;
        try { p.Cts.Cancel(); } catch (ObjectDisposedException) { }
    }

    public OAuthSignInStatus GetStatus(int mailboxId) =>
        _pending.TryGetValue(mailboxId, out var p) ? p.Status : new OAuthSignInStatus(OAuthSignInState.None);

    public bool IsSignedIn(Mailbox mailbox) => !string.IsNullOrEmpty(mailbox.EncryptedOAuthCache);

    public async Task SignOutAsync(int mailboxId, CancellationToken ct)
    {
        if (_pending.TryRemove(mailboxId, out var p)) Cancel(p);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var mailbox = await db.Mailboxes.FirstOrDefaultAsync(m => m.Id == mailboxId, ct);
        if (mailbox is null) return;
        mailbox.EncryptedOAuthCache = null;
        await db.SaveChangesAsync(ct);
    }
}
