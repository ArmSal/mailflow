using System.Security.Cryptography;
using System.Text;
using Mailflow.Application.Updates;

namespace Mailflow.Application.Tests;

public class UpdateManifestTests
{
    private static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static string Pub(ECDsa k) => Convert.ToBase64String(k.ExportSubjectPublicKeyInfo());

    private static (byte[] Json, string Sig) Make(ECDsa key, string version = "1.4.0", string? sha = null, long size = 5, string? name = null)
    {
        sha ??= new string('a', 64);
        var json = Encoding.UTF8.GetBytes($$"""
            {"version":"{{version}}","published":"2026-11-27T10:00:00Z","files":[
              {"name":"{{name ?? $"Mailflow-Setup-{version}-win-x64.exe"}}","sha256":"{{sha}}","size":{{size}}},
              {"name":"MailflowPrintAgent-Setup-{{version}}-win-x64.exe","sha256":"{{sha}}","size":{{size}}}]}
            """);
        return (json, Convert.ToBase64String(key.SignData(json, HashAlgorithmName.SHA256)));
    }

    [Fact]
    public void Valid_signature_gives_version_and_files_for_both_products()
    {
        var (json, sig) = Make(Key);
        var m = UpdateManifest.TryParse(json, sig, Pub(Key));
        Assert.NotNull(m);
        Assert.Equal(new Version(1, 4, 0), m!.Version);
        Assert.Equal("Mailflow-Setup-1.4.0-win-x64.exe", m.FileFor(UpdateProduct.Mailflow)!.Name);
        Assert.Equal("MailflowPrintAgent-Setup-1.4.0-win-x64.exe", m.FileFor(UpdateProduct.PrintAgent)!.Name);
    }

    [Fact]
    public void Tampered_content_is_rejected()
    {
        var (json, sig) = Make(Key);
        var tampered = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(json).Replace("1.4.0", "9.9.9"));
        Assert.Null(UpdateManifest.TryParse(tampered, sig, Pub(Key)));
    }

    [Fact]
    public void Signature_from_another_key_is_rejected()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (json, sig) = Make(other);
        Assert.Null(UpdateManifest.TryParse(json, sig, Pub(Key)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64 !!")]
    public void Garbage_signature_is_rejected(string sig)
    {
        var (json, _) = Make(Key);
        Assert.Null(UpdateManifest.TryParse(json, sig, Pub(Key)));
    }

    [Fact]
    public void Signed_but_malformed_json_is_rejected()
    {
        var json = Encoding.UTF8.GetBytes("""{"version":"1.4.0"}""");
        var sig = Convert.ToBase64String(Key.SignData(json, HashAlgorithmName.SHA256));
        Assert.Null(UpdateManifest.TryParse(json, sig, Pub(Key)));
    }

    [Fact]
    public void A_file_with_an_unexpected_name_is_not_offered()
    {
        var (json, sig) = Make(Key, name: "evil.exe");
        var m = UpdateManifest.TryParse(json, sig, Pub(Key))!;
        Assert.Null(m.FileFor(UpdateProduct.Mailflow));
        Assert.NotNull(m.FileFor(UpdateProduct.PrintAgent));
    }

    [Fact]
    public void A_bad_hash_is_not_offered()
    {
        var (json, sig) = Make(Key, sha: "xyz");
        Assert.Null(UpdateManifest.TryParse(json, sig, Pub(Key))!.FileFor(UpdateProduct.Mailflow));
    }

    [Fact]
    public void Matches_checks_size_and_hash()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "hello");
            var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("hello"))).ToLowerInvariant();
            Assert.True(UpdateManifest.Matches(path, new ManifestFile("x", sha, 5)));
            Assert.False(UpdateManifest.Matches(path, new ManifestFile("x", sha, 6)));
            Assert.False(UpdateManifest.Matches(path, new ManifestFile("x", new string('0', 64), 5)));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Embedded_public_key_is_a_valid_P256_key()
    {
        using var k = ECDsa.Create();
        k.ImportSubjectPublicKeyInfo(Convert.FromBase64String(UpdateKeys.Public), out _);
        Assert.Equal(256, k.KeySize);
    }
}
