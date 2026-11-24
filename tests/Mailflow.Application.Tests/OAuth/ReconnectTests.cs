using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;
using NSubstitute;

namespace Mailflow.Application.Tests.OAuth;

public class ReconnectTests
{
    [Fact]
    public async Task Mailbox_needing_reconnection_records_its_error_and_does_not_stop_the_others()
    {
        using var f = new Fixture();
        var oauth = new Mailbox { Id = 2, StoreId = 1, Email = "b@example.com", AuthMethod = AuthMethod.MicrosoftOAuth };
        f.Db.Mailboxes.Add(oauth);
        f.Db.SaveChanges();
        f.Connections.CreateAsync(oauth, Arg.Any<CancellationToken>())
            .Returns<Task<MailboxConnection>>(_ => throw new OAuthReconnectRequiredException("Reconnexion Microsoft requise : test"));
        f.Fetch(Fixture.Msg());

        var logs = await f.Processor.RunAllAsync(default);

        Assert.Equal(2, logs.Count);
        var bad = logs.Single(l => l.MailboxId == 2);
        Assert.Contains("Reconnexion Microsoft requise", bad.Error);
        Assert.Contains("Reconnexion Microsoft requise", oauth.LastError);
        var good = logs.Single(l => l.MailboxId == 1);
        Assert.Null(good.Error);
        Assert.Equal(1, good.DocumentsSaved);
    }
}
