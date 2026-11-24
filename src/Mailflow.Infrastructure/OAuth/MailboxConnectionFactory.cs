using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Infrastructure.OAuth;

internal sealed class MailboxConnectionFactory(IAppDbContext db, ISecretStore secrets, IMsalClient msal) : IMailboxConnectionFactory
{
    public const string ReconnectMessage = "Reconnexion Microsoft requise : ouvrez la boîte mail dans le tableau de bord et reconnectez-vous.";

    public async Task<MailboxConnection> CreateAsync(Mailbox mailbox, CancellationToken ct)
    {
        if (mailbox.AuthMethod != AuthMethod.MicrosoftOAuth)
            return new MailboxConnection(mailbox.Host, mailbox.Port, mailbox.UseSsl, mailbox.Username, secrets.Unprotect(mailbox.EncryptedPassword));

        var clientId = await OAuthSettings.GetClientIdAsync(db, ct);
        if (clientId is null || string.IsNullOrEmpty(mailbox.EncryptedOAuthCache)) throw new OAuthReconnectRequiredException(ReconnectMessage);

        var username = string.IsNullOrEmpty(mailbox.Username) ? mailbox.Email : mailbox.Username;
        SilentResult r;
        try
        {
            r = await msal.AcquireSilentAsync(clientId, secrets.Unprotect(mailbox.EncryptedOAuthCache), username, ct);
        }
        catch (OAuthInteractionRequiredException) { throw new OAuthReconnectRequiredException(ReconnectMessage); }
        catch (System.Security.Cryptography.CryptographicException) { throw new OAuthReconnectRequiredException(ReconnectMessage); }

        var protectedCache = secrets.Protect(r.NewCacheBlob);
        if (protectedCache != mailbox.EncryptedOAuthCache)
        {
            var row = await db.Mailboxes.FirstOrDefaultAsync(m => m.Id == mailbox.Id, ct);
            if (row is not null) { row.EncryptedOAuthCache = protectedCache; await db.SaveChangesAsync(ct); }
            mailbox.EncryptedOAuthCache = protectedCache;
        }
        return new MailboxConnection(mailbox.Host, mailbox.Port, mailbox.UseSsl, username, "", r.AccessToken);
    }
}
