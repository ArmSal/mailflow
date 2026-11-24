using Mailflow.Application.Processing;

namespace Mailflow.Web.Worker;

/// <summary>The worker is a singleton; MailboxProcessor needs a scoped DbContext, so each pass gets its own scope.</summary>
public sealed class ScopedMailboxRunner(IServiceScopeFactory scopes, ILogger<ScopedMailboxRunner> log) : IMailboxRunner
{
    public async Task RunAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var runs = await scope.ServiceProvider.GetRequiredService<MailboxProcessor>().RunAllAsync(ct);
        foreach (var run in runs.Where(r => r.Error != null))
            log.LogWarning("Mailbox {MailboxId} run finished with an error: {Error}", run.MailboxId, run.Error);
    }
}
