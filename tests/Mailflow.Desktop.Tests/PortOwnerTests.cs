using System.Net;
using System.Net.Sockets;
using Mailflow.Desktop;

namespace Mailflow.Desktop.Tests;

public class PortOwnerTests
{
    private const string Me = @"C:\Apps\Mailflow\Mailflow.exe";
    private static Task<string?> Banner(CancellationToken _) => Task.FromResult<string?>("Mailflow");

    [Theory]
    [InlineData(@"C:\Apps\Mailflow\Mailflow.exe", ServerMode.Attach)]
    [InlineData(@"c:\apps\mailflow\MAILFLOW.EXE", ServerMode.Attach)]
    [InlineData(@"C:\Apps\Mailflow\..\Mailflow\Mailflow.exe", ServerMode.Attach)]
    [InlineData(@"C:\Other\Mailflow.exe", ServerMode.Refuse)]
    [InlineData(@"C:\Apps\Mailflow\Mailflow.exe.bak", ServerMode.Refuse)]
    [InlineData(null, ServerMode.Refuse)]
    [InlineData("", ServerMode.Refuse)]
    public async Task BannerAnswers_DecisionDependsOnOwner(string? owner, ServerMode expected) =>
        Assert.Equal(expected, await AttachOrStart.DecideAsync(Banner, () => owner, Me));

    [Fact]
    public async Task BannerAnswers_OwnerLookupThrowsOrCurrentUnknown_Refuses()
    {
        Assert.Equal(ServerMode.Refuse, await AttachOrStart.DecideAsync(Banner, () => throw new InvalidOperationException(), Me));
        Assert.Equal(ServerMode.Refuse, await AttachOrStart.DecideAsync(Banner, () => Me, null));
    }

    [Fact]
    public async Task NoBanner_StartsWithoutAskingOwner()
    {
        var asked = false;
        string? Owner() { asked = true; return null; }
        Assert.Equal(ServerMode.Start, await AttachOrStart.DecideAsync(_ => Task.FromResult<string?>("other"), Owner, Me));
        Assert.Equal(ServerMode.Start, await AttachOrStart.DecideAsync(_ => Task.FromResult<string?>(null), Owner, Me));
        Assert.Equal(ServerMode.Start, await AttachOrStart.DecideAsync(_ => throw new HttpRequestException(), Owner, Me));
        Assert.False(asked);
    }

    [Fact]
    public void SamePath_IgnoresCaseAndTrailingSeparator()
    {
        Assert.True(AttachOrStart.SamePath(@"C:\A\b.exe", @"c:\a\B.EXE"));
        Assert.True(AttachOrStart.SamePath(@"C:\A\b.exe\", @"C:\A\b.exe"));
        Assert.False(AttachOrStart.SamePath(@"C:\A\b.exe", @"C:\A\c.exe"));
        Assert.False(AttachOrStart.SamePath(null, null));
    }

    [Fact]
    public async Task Flow_Refused_ShowsFrenchMessageAndExitsNonZero()
    {
        string? shown = null; var started = false;
        var o = await StartupFlow.RunAsync(5088, Banner, () => @"C:\Evil\x.exe", Me, () => { started = true; return Task.CompletedTask; }, m => shown = m);
        Assert.Equal(StartupOutcome.Refused, o);
        Assert.NotEqual(0, StartupFlow.ExitCode(o));
        Assert.False(started);
        Assert.Contains("Le port 5088 est utilisé par un autre programme que Mailflow", shown);
        Assert.Contains("protéger votre mot de passe", shown);
    }

    [Fact]
    public async Task Flow_AttachAndStart_ExitZero_NoDialog()
    {
        var shown = false; var started = false;
        var a = await StartupFlow.RunAsync(1, Banner, () => Me, Me, () => { started = true; return Task.CompletedTask; }, _ => shown = true);
        Assert.Equal(StartupOutcome.Attached, a);
        Assert.False(started);
        var s = await StartupFlow.RunAsync(1, _ => Task.FromResult<string?>(null), () => null, Me, () => { started = true; return Task.CompletedTask; }, _ => shown = true);
        Assert.Equal(StartupOutcome.Started, s);
        Assert.True(started);
        Assert.False(shown);
        Assert.Equal(0, StartupFlow.ExitCode(a) + StartupFlow.ExitCode(s));
    }

    [Fact]
    public async Task Flow_PortBusyOnStart_ShowsClearMessage()
    {
        string? shown = null;
        var inner = new SocketException((int)SocketError.AddressAlreadyInUse);
        var o = await StartupFlow.RunAsync(5088, _ => Task.FromResult<string?>(null), () => null, Me,
            () => throw new IOException("Failed to bind", inner), m => shown = m);
        Assert.Equal(StartupOutcome.PortBusy, o);
        Assert.NotEqual(0, StartupFlow.ExitCode(o));
        Assert.Contains("Le port 5088 est déjà utilisé", shown);
    }

    [Fact]
    public async Task Flow_OtherStartFailure_IsGenericAndNonZero()
    {
        string? shown = null;
        var o = await StartupFlow.RunAsync(5088, _ => Task.FromResult<string?>(null), () => null, Me,
            () => throw new InvalidOperationException("boom"), m => shown = m);
        Assert.Equal(StartupOutcome.Failed, o);
        Assert.Contains("boom", shown);
        Assert.NotEqual(0, StartupFlow.ExitCode(o));
    }

    [Fact]
    public void RealLookup_ReturnsThisProcessForItsOwnListener()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        try
        {
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            Assert.True(AttachOrStart.SamePath(PortOwner.GetListenerImagePath(port), Environment.ProcessPath));
        }
        finally { l.Stop(); }
        var any = new TcpListener(IPAddress.Any, 0); any.Start();
        try { Assert.True(AttachOrStart.SamePath(PortOwner.GetListenerImagePath(((IPEndPoint)any.LocalEndpoint).Port), Environment.ProcessPath)); }
        finally { any.Stop(); }
        var v6 = new TcpListener(IPAddress.IPv6Loopback, 0); v6.Start();
        try { Assert.True(AttachOrStart.SamePath(PortOwner.GetListenerImagePath(((IPEndPoint)v6.LocalEndpoint).Port), Environment.ProcessPath)); }
        finally { v6.Stop(); }
    }

    [Fact]
    public void RealLookup_NobodyListening_ReturnsNull()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
        Assert.Null(PortOwner.GetListenerImagePath(port));
    }
}
