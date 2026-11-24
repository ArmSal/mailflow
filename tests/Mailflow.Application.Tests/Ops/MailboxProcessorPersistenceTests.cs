using Mailflow.Application.Abstractions;
using NSubstitute;

namespace Mailflow.Application.Tests.Ops;

public class MailboxProcessorPersistenceTests
{
    [Fact]
    public async Task Run_persists_RunLog_and_updates_mailbox_status()
    {
        using var f = new Fixture();
        f.Fetch(Fixture.Msg());

        var log = await f.Processor.RunAsync(f.Mailbox, default);

        var saved = Assert.Single(f.Db.RunLogs);
        Assert.Equal((f.Mailbox.Id, 1, 1, 1), (saved.MailboxId!.Value, saved.MessagesHandled, saved.DocumentsSaved, saved.Printed));
        Assert.Equal(log.Id, saved.Id);
        Assert.NotNull(f.Mailbox.LastCheckAt);
        Assert.Null(f.Mailbox.LastError);
    }

    [Fact]
    public async Task Mailbox_action_failure_is_surfaced_in_RunLog_and_LastError()
    {
        using var f = new Fixture();
        f.Fetch(Fixture.Msg());
        f.Mail.MarkAsReadAsync(default!, default!, default, default).ReturnsForAnyArgs(Task.FromException(new IOException("imap down")));

        var log = await f.Processor.RunAsync(f.Mailbox, default);

        Assert.Contains("imap down", log.Error);
        Assert.Contains("imap down", f.Db.RunLogs.Single().Error);
        Assert.Contains("imap down", f.Mailbox.LastError);
        Assert.Equal(1, log.DocumentsSaved);
    }
}
