namespace Mailflow.Web.Pages.Mailboxes;

public static class MailErrorHints
{
    /// <summary>French hint for common IMAP connection/authentication errors, else null.</summary>
    public static string? For(string? error, string? host = null)
    {
        if (string.IsNullOrEmpty(error)) return null;

        var refused = error.Contains("LOGIN command is disabled", StringComparison.OrdinalIgnoreCase)
            || error.Contains("basic authentication", StringComparison.OrdinalIgnoreCase)
            || (error.Contains("AUTHENTICATE", StringComparison.OrdinalIgnoreCase) && error.Contains("disabled", StringComparison.OrdinalIgnoreCase));
        if (refused)
            return "Ce serveur refuse la connexion par mot de passe : activez l'accès IMAP ou utilisez un mot de passe d'application.";

        var isAppPasswordHost = !string.IsNullOrEmpty(host)
            && (host.Contains("gmail.com", StringComparison.OrdinalIgnoreCase)
                || host.Contains("mail.me.com", StringComparison.OrdinalIgnoreCase)
                || host.Contains("yahoo.com", StringComparison.OrdinalIgnoreCase));
        if (error.Contains("Application-specific password", StringComparison.OrdinalIgnoreCase)
            || error.Contains("Web login required", StringComparison.OrdinalIgnoreCase)
            || (isAppPasswordHost && (error.Contains("Invalid credentials", StringComparison.OrdinalIgnoreCase)
                || error.Contains("AUTHENTICATIONFAILED", StringComparison.OrdinalIgnoreCase)
                || error.Contains("LOGIN failed", StringComparison.OrdinalIgnoreCase))))
        {
            return "Authentification refusée : Gmail, iCloud et Yahoo exigent un mot de passe d'application (et non le mot de passe habituel) ainsi que l'accès IMAP activé.";
        }

        if (error.Contains("SSL", StringComparison.OrdinalIgnoreCase)
            || error.Contains("TLS", StringComparison.OrdinalIgnoreCase)
            || error.Contains("handshake", StringComparison.OrdinalIgnoreCase)
            || error.Contains("wrong version number", StringComparison.OrdinalIgnoreCase))
        {
            return "Échec de connexion sécurisée (SSL/TLS) : utilisez le port 993 avec SSL activé, ou le port 143 avec SSL désactivé (STARTTLS).";
        }

        if (error.Contains("No such host", StringComparison.OrdinalIgnoreCase)
            || error.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase)
            || error.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || error.Contains("actively refused", StringComparison.OrdinalIgnoreCase))
        {
            return "Serveur IMAP injoignable : vérifiez le nom du serveur, le port et la connexion internet.";
        }

        return null;
    }
}
