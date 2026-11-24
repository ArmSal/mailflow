using Mailflow.Desktop;

namespace Mailflow.Desktop.Tests;

public class DesktopLogicTests
{
    private static readonly Uri Origin = new("http://127.0.0.1:5088");

    [Theory]
    [InlineData("http://127.0.0.1:5088/", true)]
    [InlineData("http://127.0.0.1:5088/History?handler=Pdf&id=3", true)]
    [InlineData("http://127.0.0.1:5099/", false)]
    [InlineData("http://example.com/", false)]
    [InlineData("https://127.0.0.1:5088/", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///C:/Windows/win.ini", false)]
    [InlineData("", false)]
    public void AllowList(string uri, bool allowed) => Assert.Equal(allowed, NavigationPolicy.IsAllowed(uri, Origin));

    [Fact]
    public void OnlyForeignWebLinksAreExternal()
    {
        Assert.True(NavigationPolicy.IsExternalWeb("https://example.com/", Origin));
        Assert.False(NavigationPolicy.IsExternalWeb("javascript:alert(1)", Origin));
        Assert.False(NavigationPolicy.IsExternalWeb("http://127.0.0.1:5088/x", Origin));
    }

    [Fact]
    public void ParsesSwitchesAndKeepsOtherArgs()
    {
        var o = LaunchOptions.Parse(["--Minimized", "--urls", "http://x"]);
        Assert.True(o.Minimized);
        Assert.False(o.Headless);
        Assert.Equal(["--urls", "http://x"], o.HostArgs);
        Assert.True(LaunchOptions.Parse(["--headless"]).Headless);
        Assert.False(LaunchOptions.Parse([]).Minimized);
    }

    [Fact]
    public async Task AttachesOnlyWhenHealthAnswersMailflow()
    {
        Assert.Equal(ServerMode.Attach, await AttachOrStart.DecideAsync(_ => Task.FromResult<string?>("Mailflow")));
        Assert.Equal(ServerMode.Start, await AttachOrStart.DecideAsync(_ => Task.FromResult<string?>("other")));
        Assert.Equal(ServerMode.Start, await AttachOrStart.DecideAsync(_ => throw new HttpRequestException()));
    }

    [Fact]
    public void WindowStateRoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "w.json");
        var s = new WindowState(10, 20, 1100, 700, true);
        s.Save(path);
        Assert.Equal(s, WindowState.Load(path));
        Assert.Null(WindowState.Load(path + ".missing"));
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }
}
