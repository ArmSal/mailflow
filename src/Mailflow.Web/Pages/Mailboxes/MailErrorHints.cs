namespace Mailflow.Web.Pages.Mailboxes;

public static class MailErrorHints
{
    /// <summary>French hint for raw server errors meaning "password login is refused", else null.</summary>
    public static string? For(string? error)
    {
        if (string.IsNullOrEmpty(error)) return null;
        var refused = error.Contains("LOGIN command is disabled", StringComparison.OrdinalIgnoreCase)
            || error.Contains("basic authentication", StringComparison.OrdinalIgnoreCase)
            || (error.Contains("AUTHENTICATE", StringComparison.OrdinalIgnoreCase) && error.Contains("disabled", StringComparison.OrdinalIgnoreCase));
        return refused ? "Ce serveur refuse la connexion par mot de passe. Pour un compte Microsoft, choisissez « Compte Microsoft (OAuth2) »." : null;
    }
}
