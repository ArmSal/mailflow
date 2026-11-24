using System.Security.Cryptography;
using System.Text;
using Mailflow.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Mailflow.Infrastructure.Storage;

public sealed class DocumentStoreOptions
{
    public string DocumentsRoot { get; set; } = "";
    public string FileNamePattern { get; set; } = "{Category}/{yyyy}/{MM}/{yyyy-MM-dd}_{Supplier}_{OriginalName}.pdf";
}

/// <summary>
/// Pattern tokens: {Category}, {Supplier}, {OriginalName} (without extension), and any
/// DateTime format string in braces ({yyyy}, {MM}, {yyyy-MM-dd}...). '/' separates folders.
/// </summary>
public sealed class FileSystemDocumentStore(IOptions<DocumentStoreOptions> options, IServiceScopeFactory? scopes = null) : IDocumentStore
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Current DocumentsRoot / FileNamePattern: Setting table first, configured options as fallback.</summary>
    private async Task<(string Root, string Pattern)> ResolveAsync(CancellationToken ct)
    {
        string? root = null, pattern = null;
        if (scopes is not null)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetService<IAppDbContext>();
            if (db is not null)
            {
                var values = await db.Settings.AsNoTracking()
                    .Where(s => s.Key == "DocumentsRoot" || s.Key == "FileNamePattern")
                    .ToDictionaryAsync(s => s.Key, s => s.Value, ct);
                root = values.GetValueOrDefault("DocumentsRoot");
                pattern = values.GetValueOrDefault("FileNamePattern");
            }
        }
        root = string.IsNullOrWhiteSpace(root) ? options.Value.DocumentsRoot : root;
        pattern = string.IsNullOrWhiteSpace(pattern) ? options.Value.FileNamePattern : pattern;
        if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("DocumentsRoot is not configured.");
        return (Path.GetFullPath(root), pattern);
    }

    public async Task<StoredDocument> SaveAsync(byte[] content, string categorySubfolder, string supplier, string originalName, DateTime receivedAt, CancellationToken ct)
    {
        var sha = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var (root, pattern) = await ResolveAsync(ct);
        var relative = BuildRelativePath(pattern, categorySubfolder, supplier, originalName, receivedAt);

        await Gate.WaitAsync(ct);
        try
        {
            var existing = FindByContent(root, content, sha);
            if (existing is not null) return new StoredDocument(existing, sha, true);

            var target = EnsureInside(root, Path.Combine(root, relative));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            target = UniquePath(target);
            await File.WriteAllBytesAsync(target, content, ct);
            return new StoredDocument(target, sha, false);
        }
        finally { Gate.Release(); }
    }

    public async Task<string> MoveAsync(string storedPath, string newCategorySubfolder, CancellationToken ct)
    {
        var (root, _) = await ResolveAsync(ct);
        var source = EnsureInside(root, Path.GetFullPath(storedPath));
        if (!File.Exists(source)) throw new FileNotFoundException("Stored document not found.", source);

        var rel = Path.GetRelativePath(root, source).Split(Path.DirectorySeparatorChar);
        rel[0] = SanitizeSegment(newCategorySubfolder);
        var target = EnsureInside(root, Path.Combine(root, Path.Combine(rel)));
        if (string.Equals(target, source, StringComparison.OrdinalIgnoreCase)) return source;

        await Gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var destination = UniquePath(target);
            File.Move(source, destination);
            return destination;
        }
        finally { Gate.Release(); }
    }

    internal static string BuildRelativePath(string pattern, string category, string supplier, string originalName, DateTime date)
    {
        var name = Path.GetFileNameWithoutExtension(originalName.Replace('\\', '/').Split('/')[^1]);
        var text = pattern.Replace("{Category}", "\u0001C\u0002").Replace("{Supplier}", "\u0001S\u0002").Replace("{OriginalName}", "\u0001N\u0002");
        var segments = new List<string>();
        foreach (var raw in text.Split('/', '\\'))
        {
            if (raw.Length == 0) continue;
            var sb = new StringBuilder();
            for (var i = 0; i < raw.Length; i++)
            {
                if (raw[i] == '\u0001')
                {
                    var value = raw[i + 1] switch { 'C' => category, 'S' => supplier, _ => name };
                    sb.Append(SanitizeSegment(value));
                    i += 2;
                }
                else if (raw[i] == '{' && raw.IndexOf('}', i) is var end and > 0)
                {
                    sb.Append(date.ToString(raw[(i + 1)..end], System.Globalization.CultureInfo.InvariantCulture));
                    i = end;
                }
                else sb.Append(raw[i]);
            }
            segments.Add(SanitizeSegment(sb.ToString()));
        }
        if (segments.Count == 0) segments.Add("_");
        return Path.Combine([.. segments]);
    }

    internal static string SanitizeSegment(string? value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var c in value ?? "")
            sb.Append(c is '/' or '\\' or ':' or '<' or '>' or '"' or '|' or '?' or '*' || Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        var s = sb.ToString().Trim().TrimStart('.').TrimEnd('.', ' ');
        if (s.Length == 0) return "_";
        var stem = s.Split('.')[0].Trim();
        if (Reserved.Contains(stem)) s = "_" + s;
        return s;
    }

    private static string EnsureInside(string root, string path)
    {
        var full = Path.GetFullPath(path);
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Path escapes the documents root.");
        return full;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var n = 2; ; n++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({n}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static string? FindByContent(string root, byte[] content, string sha)
    {
        if (!Directory.Exists(root)) return null;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (new FileInfo(file).Length != content.Length) continue;
            using var fs = File.OpenRead(file);
            if (Convert.ToHexString(SHA256.HashData(fs)).Equals(sha, StringComparison.OrdinalIgnoreCase)) return file;
        }
        return null;
    }
}
