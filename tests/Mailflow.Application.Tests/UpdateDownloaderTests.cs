using System.Net;
using System.Security.Cryptography;
using System.Text;
using Mailflow.Application.Updates;

namespace Mailflow.Application.Tests;

public class UpdateDownloaderTests : IDisposable
{
    private const string Base = "https://example.test/dl/";
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _work = Path.Combine(Path.GetTempPath(), "mf-dl-" + Guid.NewGuid().ToString("N"));
    private string Pub => Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());
    public void Dispose() { try { Directory.Delete(_work, true); } catch (IOException) { } }

    private sealed class Stub(Dictionary<string, byte[]> files) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            => Task.FromResult(files.TryGetValue(r.RequestUri!.ToString(), out var b)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(b) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private (HttpClient Http, byte[] Installer) Release(string version = "1.4.0", byte[]? served = null, ECDsa? signer = null, string? tag = null)
    {
        var installer = Encoding.UTF8.GetBytes("pretend installer");
        var sha = Convert.ToHexString(SHA256.HashData(installer)).ToLowerInvariant();
        var name = UpdateManifest.InstallerName(UpdateProduct.Mailflow, Version.Parse(version));
        var json = Encoding.UTF8.GetBytes($$"""{"version":"{{version}}","published":"x","files":[{"name":"{{name}}","sha256":"{{sha}}","size":{{installer.Length}}}]}""");
        var sig = Encoding.ASCII.GetBytes(Convert.ToBase64String((signer ?? _key).SignData(json, HashAlgorithmName.SHA256)));
        var root = Base + (tag ?? "v" + version) + "/";
        return (new HttpClient(new Stub(new()
        {
            [root + "manifest.json"] = json, [root + "manifest.json.sig"] = sig, [root + name] = served ?? installer,
        })), installer);
    }

    private Task<PrepareResult> Run(HttpClient http, string tag = "v1.4.0", string current = "1.3.3.0")
        => UpdateDownloader.PrepareAsync(http, UpdateProduct.Mailflow, tag, Version.Parse(current), _work, default, Pub, Base);

    [Fact]
    public async Task Good_release_gives_a_verified_installer_on_disk()
    {
        var (http, installer) = Release();
        var r = await Run(http);
        Assert.Equal(PrepareError.None, r.Error);
        Assert.Equal(installer, File.ReadAllBytes(r.InstallerPath!));
    }

    [Fact]
    public async Task Tampered_installer_is_rejected_and_not_kept()
    {
        var (http, _) = Release(served: Encoding.UTF8.GetBytes("pretend installeR"));
        var r = await Run(http);
        Assert.Equal(PrepareError.HashMismatch, r.Error);
        Assert.Empty(Directory.GetFiles(_work, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Oversized_download_is_rejected()
    {
        var (http, _) = Release(served: new byte[1000]);
        Assert.Equal(PrepareError.HashMismatch, (await Run(http)).Error);
    }

    [Fact]
    public async Task Manifest_signed_by_another_key_is_rejected()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (http, _) = Release(signer: other);
        Assert.Equal(PrepareError.BadSignature, (await Run(http)).Error);
    }

    [Fact]
    public async Task Old_signed_manifest_replayed_under_a_new_tag_is_rejected()
    {
        var (http, _) = Release(version: "1.2.0", tag: "v1.4.0");
        Assert.Equal(PrepareError.BadSignature, (await Run(http)).Error);
    }

    [Fact]
    public async Task Same_or_older_version_is_not_installed()
    {
        var (http, _) = Release();
        Assert.Equal(PrepareError.NotNewer, (await Run(http, current: "1.4.0.0")).Error);
        Assert.Equal(PrepareError.NotNewer, (await Run(http, current: "2.0.0.0")).Error);
    }

    [Fact]
    public async Task Release_without_a_manifest_is_reported_as_such()
        => Assert.Equal(PrepareError.NoManifest, (await Run(new HttpClient(new Stub(new())))).Error);

    [Theory]
    [InlineData("1.4.0")]
    [InlineData("v1.4.0/../x")]
    [InlineData("v1.4")]
    public async Task Malformed_tag_never_reaches_the_network(string tag)
        => Assert.Equal(PrepareError.InvalidTag, (await Run(Release().Http, tag)).Error);
}
