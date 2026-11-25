using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;
using Mailflow.Web.Localization;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Web.Pages.Settings;

public static class SettingsRules
{
    public const string RootKey = "DocumentsRoot", PatternKey = "FileNamePattern", HoursKey = "PrintMaxAgeHours", SenderCheckKey = "PrintSenderCheck", PrintEmailBodyKey = "PrintEmailBodyWithoutPdf";

    private static bool HasTraversal(string s) => s.Split('/', '\\').Any(p => p == "..");

    /// <summary>Absolute, no '..'; creates the folder and checks it is writable. Returns an error or null.</summary>
    public static string? ValidateRoot(string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return Loc.T("Le dossier des documents est obligatoire.");
        root = root.Trim();
        if (!Path.IsPathFullyQualified(root)) return Loc.T("Le dossier des documents doit être un chemin absolu.");
        if (HasTraversal(root)) return Loc.T("Le chemin ne doit pas contenir « .. ».");
        try
        {
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Loc.T("Dossier inaccessible en écriture :") + " " + ex.Message;
        }
        return null;
    }

    public static string? ValidatePattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return Loc.T("Le modèle de nom de fichier est obligatoire.");
        if (!pattern.Contains("{OriginalName}")) return Loc.T("Le modèle doit contenir {OriginalName}.");
        if (HasTraversal(pattern)) return Loc.T("Le modèle ne doit pas contenir « .. ».");
        if (Path.IsPathRooted(pattern) || pattern.StartsWith('/') || pattern.StartsWith('\\') || pattern.Split('/', '\\').Any(p => p.Contains(':')))
            return Loc.T("Le modèle ne doit pas être un chemin absolu.");
        return null;
    }

    public static string? ValidateHours(int hours)
        => hours is < 1 or > 168 ? Loc.T("La durée doit être comprise entre 1 et 168 heures.") : null;

    /// <summary>False unless explicitly enabled ("true").</summary>
    public static bool ParsePrintEmailBody(string? value)
        => string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Stored enum name; missing or unknown values mean Warn.</summary>
    public static SenderCheckMode ParseSenderCheck(string? value)
        => Enum.TryParse<SenderCheckMode>(value, out var m) && Enum.IsDefined(m) && !int.TryParse(value, out _) ? m : SenderCheckMode.Warn;

    public static string? ValidateSenderCheck(string? value)
        => Enum.TryParse<SenderCheckMode>(value, out var m) && Enum.IsDefined(m) && !int.TryParse(value, out _) ? null : Loc.T("Choisissez un mode de vérification valide.");

    public static string? ValidateNewPassword(string? password, string? confirm)
    {
        if ((password ?? "").Length < 8) return Loc.T("Le nouveau mot de passe doit contenir au moins 8 caractères.");
        if (password != confirm) return Loc.T("Les deux mots de passe ne correspondent pas.");
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
