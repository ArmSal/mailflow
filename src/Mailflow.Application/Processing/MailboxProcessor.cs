using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Application.Processing;

public sealed class MailboxProcessor(IAppDbContext db, IMailClient mail, IMailboxConnectionFactory connections, MessagePipeline pipeline, IClock clock)
{
    /// <summary>Runs every active mailbox; one failing mailbox never stops the others.</summary>
    public async Task<IReadOnlyList<RunLog>> RunAllAsync(CancellationToken ct)
    {
        var mailboxes = await db.Mailboxes.Where(m => m.IsActive).ToListAsync(ct);
        var logs = new List<RunLog>();
        foreach (var mailbox in mailboxes)
            logs.Add(await RunAsync(mailbox, ct));
        return logs;
    }

    public async Task<RunLog> RunAsync(Mailbox mailbox, CancellationToken ct)
    {
        var started = clock.UtcNow;
        var log = new RunLog { StartedAt = started, MailboxId = mailbox.Id };
        var errors = new List<string>();
        try
        {
            var connection = await connections.CreateAsync(mailbox, ct);
            var messages = await mail.FetchSinceAsync(connection, mailbox.InboxFolder, started.AddDays(-mailbox.LookbackDays), ct);
            foreach (var message in messages)
            {
                try
                {
                    var outcome = await pipeline.ProcessAsync(mailbox, connection, message, ct);
                    if (outcome.AlreadyProcessed) continue;
                    log.MessagesHandled++;
                    log.DocumentsSaved += outcome.DocumentsSaved;
                    log.Printed += outcome.Printed;
                    if (outcome.Warning != null) errors.Add(outcome.Warning);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add($"Message '{message.Subject}' will be retried: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errors.Add($"Mailbox error: {ex.Message}");
        }

        log.Error = errors.Count == 0 ? null : string.Join(" | ", errors);
        log.Duration = clock.UtcNow - started;
        mailbox.LastCheckAt = started;
        mailbox.LastError = log.Error;
        db.RunLogs.Add(log);
        await db.SaveChangesAsync(CancellationToken.None);
        return log;
    }
}
