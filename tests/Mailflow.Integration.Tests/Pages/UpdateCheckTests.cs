using Mailflow.Web.Updates;
using NSubstitute;

namespace Mailflow.Integration.Tests.Pages;

public sealed class UpdateCheckTests
{
    private static UpdateChecker Checker(ReleaseInfo? latest, out IReleaseSource source)
    {
        source = Substitute.For<IReleaseSource>();
        source.GetLatestAsync(Arg.Any<CancellationToken>()).Returns(latest);
        return new UpdateChecker(source);
    }

    [Theory]
    [InlineData("v1.2.0", "1.1.0.0", true)]
    [InlineData("1.1.1", "1.1.0.0", true)]
    [InlineData("v1.1.0", "1.1.0.0", false)]
    [InlineData("v1.0.9", "1.1.0.0", false)]
    [InlineData("v2.0.0-beta", "1.9.0.0", true)]
    [InlineData("nightly", "1.0.0.0", false)]
    public void IsNewer_compares_release_tag_with_running_version(string tag, string current, bool expected)
        => Assert.Equal(expected, UpdateChecker.IsNewer(tag, Version.Parse(current)));

    [Fact]
    public async Task Newer_release_becomes_available()
    {
        var c = Checker(new ReleaseInfo("v1.2.0", "https://github.com/x/y/releases/tag/v1.2.0"), out _);
        await c.CheckAsync(true, new Version(1, 0, 0, 0), default);
        Assert.Equal("v1.2.0", c.Available?.Tag);
    }

    [Fact]
    public async Task Same_version_or_unreachable_source_shows_nothing()
    {
        var same = Checker(new ReleaseInfo("v1.0.0", "https://x"), out _);
        await same.CheckAsync(true, new Version(1, 0, 0, 0), default);
        Assert.Null(same.Available);
        var offline = Checker(null, out _);
        await offline.CheckAsync(true, new Version(1, 0, 0, 0), default);
        Assert.Null(offline.Available);
    }

    [Fact]
    public async Task Disabled_never_calls_the_source()
    {
        var c = Checker(new ReleaseInfo("v9.0.0", "https://x"), out var source);
        await c.CheckAsync(false, new Version(1, 0, 0, 0), default);
        Assert.Null(c.Available);
        await source.DidNotReceive().GetLatestAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    public void Setting_defaults_to_on(string? value, bool expected) => Assert.Equal(expected, UpdateChecker.ParseEnabled(value));

    [Fact]
    public async Task GitHub_source_returns_null_when_offline()
    {
        var http = new HttpClient(new ThrowingHandler());
        Assert.Null(await new GitHubReleaseSource(http).GetLatestAsync(default));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new HttpRequestException("offline");
    }
}
