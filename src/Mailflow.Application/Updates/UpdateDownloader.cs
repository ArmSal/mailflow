using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Mailflow.Application.Updates;

public enum PrepareError { None, InvalidTag, NoManifest, DownloadFailed, BadSignature, NotNewer, FileMissing, HashMismatch, BackupFailed }

public sealed record PrepareResult(string? InstallerPath, PrepareError Error);

/// <summary>
/// Downloads the signed manifest and the installer of a release and checks both. Linked into the Print Agent, so BCL only.
/// Nothing is run here: the caller launches the returned installer.
/// </summary>
public static partial class UpdateDownloader
{
    public const string DefaultBaseUrl = "https://github.com/ArmSal/mailflow/releases/download/";
    private const int MaxManifestBytes = 64 * 1024;
    private const int MaxSignatureBytes = 1024;

    [GeneratedRegex(@"^v\d+\.\d+\.\d+$")]
    private static partial Regex TagPattern();

    public static async Task<PrepareResult> PrepareAsync(HttpClient http, UpdateProduct product, string tag, Version current, string workDir,
        CancellationToken ct, string publicKey = UpdateKeys.Public, string baseUrl = DefaultBaseUrl)
    {
        if (!TagPattern().IsMatch(tag)) return Fail(PrepareError.InvalidTag);
        var root = baseUrl + tag + "/";

        byte[] json, sig;
        try
        {
            json = await GetBytesAsync(http, root + "manifest.json", MaxManifestBytes, ct);
            sig = await GetBytesAsync(http, root + "manifest.json.sig", MaxSignatureBytes, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return Fail(PrepareError.NoManifest); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException) { return Fail(PrepareError.DownloadFailed); }

        var manifest = UpdateManifest.TryParse(json, Encoding.ASCII.GetString(sig), publicKey);
        // The signed version must be the tag we were told about, otherwise an old signed manifest could be replayed under a new tag.
        if (manifest is null || "v" + manifest.Version != tag) return Fail(PrepareError.BadSignature);
        if (manifest.Version <= new Version(current.Major, current.Minor, Math.Max(current.Build, 0))) return Fail(PrepareError.NotNewer);
        var file = manifest.FileFor(product);
        if (file is null) return Fail(PrepareError.FileMissing);

        var dir = Path.Combine(workDir, manifest.Version.ToString());
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, file.Name);
        var part = dest + ".part";
        try
        {
            using (var resp = await http.GetAsync(root + file.Name, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920];
                long total = 0;
                int n;
                while ((n = await src.ReadAsync(buffer, ct)) > 0)
                {
                    total += n;
                    if (total > file.Size) { dst.Close(); File.Delete(part); return Fail(PrepareError.HashMismatch); }
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                }
            }
            if (!UpdateManifest.Matches(part, file)) { File.Delete(part); return Fail(PrepareError.HashMismatch); }
            File.Move(part, dest, overwrite: true);
            return new PrepareResult(dest, PrepareError.None);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            try { File.Delete(part); } catch (IOException) { }
            return Fail(PrepareError.DownloadFailed);
        }
    }

    private static PrepareResult Fail(PrepareError e) => new(null, e);

    private static async Task<byte[]> GetBytesAsync(HttpClient http, string url, int max, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var s = await resp.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buffer = new byte[4096];
        int n;
        while ((n = await s.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + n > max) throw new InvalidDataException("response too large");
            ms.Write(buffer, 0, n);
        }
        return ms.ToArray();
    }
}
