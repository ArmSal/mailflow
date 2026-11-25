namespace Mailflow.Infrastructure.Mail;

public sealed record ImapPreset(string Name, string Host, int Port, bool UseSsl, string Note, string[]? Domains = null);

/// <summary>
/// The single list of mail providers. To support another IMAP provider, add one line here:
/// the dashboard's provider dropdown reads this list. <c>Domains</c> are the e-mail domains the form recognises.
/// </summary>
public static class ImapPresets
{
    public static IReadOnlyList<ImapPreset> All { get; } =
    [
        new("OVH MX Plan", "ssl0.ovh.net", 993, true, "Mot de passe de la boîte mail."),
        new("OVH Email Pro", "pro1.mail.ovh.net", 993, true, "Mot de passe de la boîte mail."),
        new("iCloud", "imap.mail.me.com", 993, true, "Mot de passe spécifique à l'application requis.", ["icloud.com", "me.com", "mac.com"]),
        new("Gmail", "imap.gmail.com", 993, true, "Mot de passe d'application requis.", ["gmail.com", "googlemail.com"]),
        new("Yahoo", "imap.mail.yahoo.com", 993, true, "Mot de passe d'application requis.", ["yahoo.com", "yahoo.fr", "yahoo.co.uk", "ymail.com", "rocketmail.com"]),
        new("IMAP personnalisé", "", 993, true, "Saisir l'hôte et le port manuellement."),
    ];
}
