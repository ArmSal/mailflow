using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;

namespace Mailflow.Integration.Tests.Pages;

/// <summary>Password mailboxes only: decrypts the stored password like the real factory.</summary>
public sealed class FakeConnectionFactory(ISecretStore secrets) : IMailboxConnectionFactory
{
    public Task<MailboxConnection> CreateAsync(Mailbox m, CancellationToken ct)
        => Task.FromResult(new MailboxConnection(m.Host, m.Port, m.UseSsl, m.Username, m.EncryptedPassword.Length == 0 ? "" : secrets.Unprotect(m.EncryptedPassword)));
}
