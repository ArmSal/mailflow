using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Mailflow.Agent;
using Mailflow.Application.Abstractions;
using Mailflow.Web.Agent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Mailflow.Integration.Tests.Agent;

public sealed class AgentTlsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mf-tls-" + Guid.NewGuid().ToString("N"));
    private readonly List<WebApplication> _servers = [];
    private readonly List<X509Certificate2> _certs = [];

    public AgentTlsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var s in _servers) s.StopAsync().GetAwaiter().GetResult();
        foreach (var c in _certs) c.Dispose();
        Directory.Delete(_dir, true);
    }

    /// <summary>A stand-in for DPAPI: prefixes the text so a test can see that it went through Protect.</summary>
    private static ISecretStore FakeSecrets()
    {
        var s = Substitute.For<ISecretStore>();
        s.Protect(Arg.Any<string>()).Returns(ci => "P:" + ci.Arg<string>());
        s.Unprotect(Arg.Any<string>()).Returns(ci => ci.Arg<string>()[2..]);
        return s;
    }

    private X509Certificate2 Load()
    {
        var cert = AgentTlsCertificate.LoadOrCreate(CertPath, FakeSecrets());
        _certs.Add(cert);
        return cert;
    }

    private string CertPath => Path.Combine(_dir, "agent-tls.dat");

    [Fact]
    public void The_certificate_is_created_once_and_reused_after_a_restart()
    {
        var first = Load();
        var second = Load();

        Assert.True(first.HasPrivateKey);
        Assert.Equal(AgentTlsCertificate.Fingerprint(first), AgentTlsCertificate.Fingerprint(second));
        Assert.True(first.NotAfter > DateTime.Now.AddYears(5));
    }

    [Fact]
    public void The_certificate_file_is_stored_through_the_secret_store_never_in_the_clear()
    {
        Load();
        Assert.StartsWith("P:", File.ReadAllText(CertPath));
    }

    [Fact]
    public void An_unreadable_certificate_file_is_replaced_instead_of_stopping_mailflow()
    {
        File.WriteAllText(CertPath, "P:this is not a certificate");
        var cert = Load();
        Assert.True(cert.HasPrivateKey);
    }

    [Fact]
    public void The_fingerprint_is_sha256_shown_in_pairs()
    {
        var cert = Load();
        var plain = AgentTlsCertificate.Fingerprint(cert);
        var shown = AgentTlsCertificate.Format(plain);

        Assert.Matches("^[0-9A-F]{64}$", plain);
        Assert.Matches("^([0-9A-F]{2}:){31}[0-9A-F]{2}$", shown);
        Assert.Equal(plain, AgentConfig.NormalizeFingerprint(shown));
    }

    [Theory]
    [InlineData("AB:CD ef 12", "ABCDEF12")]
    [InlineData("  ab-cd  ", "ABCD")]
    public void A_pasted_fingerprint_is_normalised(string pasted, string expected)
        => Assert.Equal(expected, AgentConfig.NormalizeFingerprint(pasted));

    [Theory]
    [InlineData("not-a-fingerprint")]
    [InlineData("AB:CD")]
    public void A_pin_that_is_not_64_hex_characters_is_refused(string pin)
        => Assert.NotNull(new AgentConfig { ServerUrl = "https://x:1", AgentName = "A", Password = "secret-secret", ServerCertSha256 = pin }.Validate());

    // ---- a real TLS handshake against an in-process Kestrel -------------------------------------------------

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private async Task<(string Url, string Fingerprint)> StartTlsServer()
    {
        var cert = Load();
        var port = FreePort();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, port, l => l.UseHttps(cert)));
        var app = builder.Build();
        app.MapGet("/api/agent/ping", () => "pong");
        await app.StartAsync();
        _servers.Add(app);
        return ($"https://127.0.0.1:{port}", AgentTlsCertificate.Fingerprint(cert));
    }

    private static AgentConfig Config(string url, string? pin) =>
        new() { ServerUrl = url, AgentName = "Boutique", Password = "secret-secret", ServerCertSha256 = pin };

    [Fact]
    public async Task The_agent_connects_when_the_server_certificate_matches_the_pin()
    {
        var (url, fingerprint) = await StartTlsServer();
        using var http = Config(url, fingerprint).NewHttpClient();

        Assert.Equal("pong", await http.GetStringAsync("api/agent/ping"));
    }

    [Fact]
    public async Task The_pin_may_be_pasted_with_colons_and_lower_case()
    {
        var (url, fingerprint) = await StartTlsServer();
        using var http = Config(url, AgentTlsCertificate.Format(fingerprint).ToLowerInvariant()).NewHttpClient();

        Assert.Equal("pong", await http.GetStringAsync("api/agent/ping"));
    }

    [Fact]
    public async Task The_agent_refuses_a_server_whose_certificate_does_not_match_the_pin()
    {
        var (url, _) = await StartTlsServer();
        using var http = Config(url, new string('A', 64)).NewHttpClient();

        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetStringAsync("api/agent/ping"));
    }

    [Fact]
    public async Task The_probe_reports_the_fingerprint_and_that_windows_does_not_trust_a_self_signed_certificate()
    {
        var (url, fingerprint) = await StartTlsServer();

        var probe = await CertificateProbe.RunAsync(new Uri(url + "/"));

        Assert.Null(probe.Error);
        Assert.Equal(fingerprint, probe.Fingerprint);
        Assert.False(probe.TrustedByWindows);
    }

    [Fact]
    public async Task The_probe_says_so_when_the_server_cannot_be_reached()
    {
        var probe = await CertificateProbe.RunAsync(new Uri($"https://127.0.0.1:{FreePort()}/"));

        Assert.NotNull(probe.Error);
        Assert.Null(probe.Fingerprint);
    }

    [Fact]
    public async Task A_self_signed_certificate_without_a_pin_is_not_trusted()
    {
        var (url, _) = await StartTlsServer();
        using var http = Config(url, null).NewHttpClient();

        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetStringAsync("api/agent/ping"));
    }
}
