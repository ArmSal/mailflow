using System.Globalization;
using Mailflow.Web.Localization;

namespace Mailflow.Integration.Tests.Pages;

public class LocalizationTests
{
    private static T InCulture<T>(string culture, Func<T> f)
    {
        var old = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        try { return f(); } finally { CultureInfo.CurrentUICulture = old; }
    }

    [Fact] public void French_returns_the_text_itself() => Assert.Equal("Historique", InCulture("fr", () => Loc.T("Historique")));
    [Fact] public void English_translates_known_text() => Assert.Equal("History", InCulture("en", () => Loc.T("Historique")));
    [Fact] public void English_falls_back_to_french_for_unknown_text() => Assert.Equal("Texte inconnu", InCulture("en", () => Loc.T("Texte inconnu")));

    [Fact]
    public void Every_english_entry_is_filled_and_keeps_its_placeholders()
    {
        Assert.NotEmpty(Loc.EnglishEntries);
        foreach (var (fr, en) in Loc.EnglishEntries)
        {
            Assert.False(string.IsNullOrWhiteSpace(en), $"Empty English text for: {fr}");
            for (var i = 0; i < 6; i++)
                Assert.True(fr.Contains("{" + i + "}") == en.Contains("{" + i + "}"), $"Placeholder {{{i}}} differs for: {fr}");
        }
    }
}
