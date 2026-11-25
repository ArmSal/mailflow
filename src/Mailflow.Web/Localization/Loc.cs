using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Mailflow.Web.Localization;

/// <summary>
/// UI translation. The French text IS the key: <c>T("Enregistrer")</c> returns it as is for French and the English
/// text from <c>Localization/en/*.json</c> (flat {"French": "English"} files, merged) for English. A missing entry falls back to French.
/// </summary>
public static class Loc
{
    public const string English = "en", French = "fr";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Dictionary = new(Load);

    public static bool IsEnglish => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == English;

    public static string T(string french) =>
        IsEnglish && Dictionary.Value.TryGetValue(french, out var english) ? english : french;

    public static string T(string french, params object?[] args) => string.Format(CultureInfo.CurrentUICulture, T(french), args);

    /// <summary>Every English entry, for tests.</summary>
    public static IReadOnlyDictionary<string, string> EnglishEntries => Dictionary.Value;

    private static IReadOnlyDictionary<string, string> Load()
    {
        var all = new Dictionary<string, string>(StringComparer.Ordinal);
        var assembly = typeof(Loc).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.Contains(".Localization.en.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            foreach (var (key, value) in JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [])
                all[key] = value;
        }
        return all;
    }
}
