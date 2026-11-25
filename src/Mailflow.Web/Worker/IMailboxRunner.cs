namespace Mailflow.Web.Worker;

/// <summary>One processing pass over the active mailboxes. A scheduled pass only runs the mailboxes that are due; "run now" runs all.</summary>
public interface IMailboxRunner
{
    Task RunAsync(CancellationToken ct, bool onlyDue = false);
}

/// <summary>Placeholder until the integrator registers the adapter around the real MailboxProcessor.</summary>
public sealed class NoopMailboxRunner : IMailboxRunner
{
    public Task RunAsync(CancellationToken ct, bool onlyDue = false) => Task.CompletedTask;
}
