using System.Security.Cryptography;
using System.Text.Json;

namespace Mailflow.Application.Updates;

public enum UpdateProduct { Mailflow, PrintAgent }

public sealed record ManifestFile(string Name, string Sha256, long Size);

/// <summary>
/// Signed description of a release (manifest.json + manifest.json.sig). Linked into the Print Agent as well, so it must
/// depend on nothing but the BCL.
/// </summary>
public sealed class UpdateManifest
{
    public Version Version { get; }
    private readonly IReadOnlyList<ManifestFile> _files;

    private UpdateManifest(Version version, IReadOnlyList<ManifestFile> files) { Version = version; _files = files; }

    /// <summary>Returns the manifest only when the signature is valid for <paramref name="publicKeyBase64"/> and the content is well formed.</summary>
    public static UpdateManifest? TryParse(byte[] json, string signatureBase64, string publicKeyBase64)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            if (!key.VerifyData(json, Convert.FromBase64String(signatureBase64.Trim()), HashAlgorithmName.SHA256)) return null;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!Version.TryParse(root.GetProperty("version").GetString(), out var version) || version.Build < 0 || version.Revision >= 0) return null;
            var files = new List<ManifestFile>();
            foreach (var f in root.GetProperty("files").EnumerateArray())
                files.Add(new ManifestFile(f.GetProperty("name").GetString() ?? "", f.GetProperty("sha256").GetString() ?? "", f.GetProperty("size").GetInt64()));
            return new UpdateManifest(version, files);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    public static string InstallerName(UpdateProduct product, Version version)
        => (product == UpdateProduct.Mailflow ? "Mailflow-Setup-" : "MailflowPrintAgent-Setup-") + $"{version}-win-x64.exe";

    /// <summary>The installer entry for a product. Only the exact expected file name is accepted, so a signed manifest cannot point elsewhere.</summary>
    public ManifestFile? FileFor(UpdateProduct product)
    {
        var f = _files.FirstOrDefault(x => x.Name == InstallerName(product, Version));
        return f is { Size: > 0 } && f.Sha256.Length == 64 && f.Sha256.All(Uri.IsHexDigit) ? f : null;
    }

    /// <summary>True when the file has the signed size and SHA-256.</summary>
    public static bool Matches(string path, ManifestFile expected)
    {
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (s.Length != expected.Size) return false;
        return string.Equals(Convert.ToHexString(SHA256.HashData(s)), expected.Sha256, StringComparison.OrdinalIgnoreCase);
    }
}
