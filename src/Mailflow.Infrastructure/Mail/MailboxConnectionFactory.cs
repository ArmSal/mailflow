using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;

namespace Mailflow.Infrastructure.Mail;

internal sealed class MailboxConnectionFactory(ISecretStore secrets) : IMailboxConnectionFactory
{
    public Task<MailboxConnection> CreateAsync(Mailbox mailbox, CancellationToken ct) =>
        Task.FromResult(new MailboxConnection(mailbox.Host, mailbox.Port, mailbox.UseSsl, mailbox.Username, secrets.Unprotect(mailbox.EncryptedPassword)));
}
