using Mailflow.Desktop;

namespace Mailflow.Desktop.Tests;

public class PortGuardTests
{
    private const string Me = @"C:\Users\x\AppData\Local\Programs\Mailflow\Mailflow.exe";

    [Fact]
    public void No_listener_is_allowed_so_the_normal_unreachable_page_shows()
        => Assert.Equal(PortVerdict.NoListener, PortGuard.Decide(0, _ => throw new InvalidOperationException("must not look up pid 0"), Me));

    [Fact]
    public void Listener_running_this_executable_is_genuine()
        => Assert.Equal(PortVerdict.Genuine, PortGuard.Decide(42, _ => Me.ToUpperInvariant(), Me));

    [Theory]
    [InlineData(@"C:\Temp\evil.exe")]
    [InlineData(@"D:\Other\Mailflow\Mailflow.exe")]   // another copy cannot be told from an impostor, same rule as at startup
    [InlineData(null)]                                // owner whose path cannot be read: fail closed
    public void Any_other_or_unreadable_owner_is_an_impostor(string? ownerPath)
        => Assert.Equal(PortVerdict.Impostor, PortGuard.Decide(42, _ => ownerPath, Me));

    private static PortVerdict Real(int port)
        => PortGuard.Decide(PortOwner.FindListenerPid(port), PortOwner.GetImagePath, Environment.ProcessPath);

    [Fact]
    public void Real_sockets_own_listener_is_genuine_and_a_closed_port_is_no_listener()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); l.Start();
        var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        try { Assert.Equal(PortVerdict.Genuine, Real(port)); }
        finally { l.Stop(); }
        Assert.Equal(PortVerdict.NoListener, Real(port));
    }

    [Fact]
    public void Real_sockets_a_listener_owned_by_another_program_is_an_impostor()
    {
        var free = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); free.Start();
        var port = ((System.Net.IPEndPoint)free.LocalEndpoint).Port; free.Stop();
        using var other = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe",
            $"-NoProfile -Command \"$l=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,{port});$l.Start();Start-Sleep 30\"")
        { CreateNoWindow = true, UseShellExecute = false })!;
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (PortOwner.FindListenerPid(port) == 0 && DateTime.UtcNow < deadline) Thread.Sleep(100);
            Assert.Equal(PortVerdict.Impostor, Real(port));
        }
        finally { other.Kill(true); }
    }

    [Fact]
    public void A_check_that_throws_counts_as_an_impostor()
        => Assert.Equal(PortVerdict.Impostor, PortGuard.Check(() => throw new InvalidOperationException()));

    [Fact]
    public void A_check_that_answers_is_passed_through()
        => Assert.Equal(PortVerdict.Genuine, PortGuard.Check(() => PortVerdict.Genuine));
}

public class PortGuardCostTests
{
    [Fact]
    public void A_real_check_is_cheap_enough_to_run_before_every_request()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); l.Start();
        try
        {
            var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
            PortGuard.Decide(PortOwner.FindListenerPid(port), PortOwner.GetImagePath, Environment.ProcessPath);   // warm up
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < 200; i++)
                Assert.Equal(PortVerdict.Genuine, PortGuard.Decide(PortOwner.FindListenerPid(port), PortOwner.GetImagePath, Environment.ProcessPath));
            Assert.True(sw.Elapsed.TotalMilliseconds / 200 < 5, $"{sw.Elapsed.TotalMilliseconds / 200:F2} ms per check");
        }
        finally { l.Stop(); }
    }
}
