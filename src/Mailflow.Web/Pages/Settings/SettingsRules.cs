using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Web.Pages.Settings;

public static class SettingsRules
{
    public const string RootKey = "DocumentsRoot", PatternKey = "FileNamePattern", HoursKey = "PrintMaxAgeHours", OAuthClientIdKey = "OAuthMicrosoftClientId";

    private static bool HasTraversal(string s) => s.Split('/', '\\').Any(p => p == "..");

    /// <summary>Absolute, no '..'; creates the folder and checks it is writable. Returns an error or null.</summary>
    public static string? ValidateRoot(string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return "Le dossier des documents est obligatoire.";
        root = root.Trim();
        if (!Path.IsPathFullyQualified(root)) return "Le dossier des documents doit être un chemin absolu.";
        if (HasTraversal(root)) return "Le chemin ne doit pas contenir « .. ».";
        try
        {
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return "Dossier inaccessible en écriture : " + ex.Message;
        }
        return null;
    }

    public static string? ValidatePattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return "Le modèle de nom de fichier est obligatoire.";
        if (!pattern.Contains("{OriginalName}")) return "Le modèle doit contenir {OriginalName}.";
        if (HasTraversal(pattern)) return "Le modèle ne doit pas contenir « .. ».";
        if (Path.IsPathRooted(pattern) || pattern.StartsWith('/') || pattern.StartsWith('\\') || pattern.Split('/', '\\').Any(p => p.Contains(':')))
            return "Le modèle ne doit pas être un chemin absolu.";
        return null;
    }

    public static string? ValidateHours(int hours)
        => hours is < 1 or > 168 ? "La durée doit être comprise entre 1 et 168 heures." : null;

    /// <summary>Empty (not configured) or a GUID.</summary>
    public static string? ValidateClientId(string? clientId)
        => string.IsNullOrWhiteSpace(clientId) || Guid.TryParse(clientId.Trim(), out _) ? null : "L'identifiant d'application doit être un GUID (par exemple 00000000-0000-0000-0000-000000000000).";

    public static string? ValidateNewPassword(string? password, string? confirm)
    {
        if ((password ?? "").Length < 8) return "Le nouveau mot de passe doit contenir au moins 8 caractères.";
        if (password != confirm) return "Les deux mots de passe ne correspondent pas.";
        return null;
    }

    public static async Task<Dictionary<string, string>> LoadAsync(IAppDbContext db, CancellationToken ct)
        => await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);

    public static async Task SaveAsync(IAppDbContext db, string key, string value, CancellationToken ct)
    {
        var s = await db.Settings.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (s is null) db.Settings.Add(new Setting { Key = key, Value = value });
        else s.Value = value;
    }
}
