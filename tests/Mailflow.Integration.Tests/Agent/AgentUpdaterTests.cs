using Mailflow.Agent;

namespace Mailflow.Integration.Tests.Agent;

public class AgentUpdaterTests
{
    private static readonly Version Cur = new(1, 3, 3, 0);

    [Theory]
    [InlineData("Auto", "v1.4.0", null, true)]
    [InlineData("auto", "v1.4.0", "v1.3.9", true)]
    [InlineData("Notify", "v1.4.0", null, false)]     // default: never installs
    [InlineData(null, "v1.4.0", null, false)]
    [InlineData("Auto", "v1.3.3", null, false)]       // same version
    [InlineData("Auto", "v1.3.0", null, false)]       // older
    [InlineData("Auto", "v1.4.0", "v1.4.0", false)]   // already tried once: no loop
    [InlineData("Auto", "nightly", null, false)]
    public void ShouldInstall_follows_mode_version_and_previous_attempt(string? mode, string tag, string? last, bool expected)
        => Assert.Equal(expected, AgentUpdater.ShouldInstall(mode, tag, Cur, last));

    [Fact]
    public void Update_mode_defaults_to_notify()
        => Assert.Equal("Notify", new AgentConfig().UpdateMode);
}
