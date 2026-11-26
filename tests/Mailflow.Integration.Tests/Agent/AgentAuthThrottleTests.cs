using Mailflow.Web.Agent;

namespace Mailflow.Integration.Tests.Agent;

public class AgentAuthThrottleTests
{
    private static readonly DateTime T0 = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private static AgentAuthThrottle Failed(int times, string ip = "1.1.1.1")
    {
        var t = new AgentAuthThrottle();
        for (var i = 0; i < times; i++) t.Fail(ip, T0);
        return t;
    }

    [Fact]
    public void A_few_failures_are_tolerated() => Assert.False(Failed(AgentAuthThrottle.MaxFailures - 1).IsBlocked("1.1.1.1", T0));

    [Fact]
    public void Too_many_failures_block_that_address()
    {
        var t = Failed(AgentAuthThrottle.MaxFailures);
        Assert.True(t.IsBlocked("1.1.1.1", T0.AddSeconds(5)));
        Assert.False(t.IsBlocked("2.2.2.2", T0.AddSeconds(5)));
    }

    [Fact]
    public void The_block_ends_when_the_window_has_passed()
    {
        var t = Failed(AgentAuthThrottle.MaxFailures);
        Assert.False(t.IsBlocked("1.1.1.1", T0 + AgentAuthThrottle.Window + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void A_successful_login_clears_the_failures()
    {
        var t = Failed(AgentAuthThrottle.MaxFailures - 1);
        t.Succeed("1.1.1.1");
        t.Fail("1.1.1.1", T0);
        Assert.False(t.IsBlocked("1.1.1.1", T0));
    }
}
