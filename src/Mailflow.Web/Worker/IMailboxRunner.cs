namespace Mailflow.Web.Worker;

/// <summary>One processing pass over all active mailboxes. The integrator adapts the real MailboxProcessor to this.</summary>
public interface IMailboxRunner
{
    Task RunAsync(CancellationToken ct);
}

/// <summary>Placeholder until the integrator registers the adapter around the real MailboxProcessor.</summary>
public sealed class NoopMailboxRunner : IMailboxRunner
{
    public Task RunAsync(CancellationToken ct) => Task.CompletedTask;
}
