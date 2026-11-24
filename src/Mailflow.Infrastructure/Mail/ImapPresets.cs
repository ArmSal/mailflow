namespace Mailflow.Infrastructure.Mail;

public sealed record ImapPreset(string Name, string Host, int Port, bool UseSsl, string Note);

/// <summary>
/// The single list of mail providers. To support another IMAP provider, add one line here:
/// the dashboard's provider dropdown reads this list.
/// </summary>
public static class ImapPresets
{
    public static IReadOnlyList<ImapPreset> All { get; } =
    [
        new("OVH MX Plan", "ssl0.ovh.net", 993, true, "Mot de passe de la boîte mail."),
        new("OVH Email Pro", "pro1.mail.ovh.net", 993, true, "Mot de passe de la boîte mail."),
        new("iCloud", "imap.mail.me.com", 993, true, "Mot de passe spécifique à l'application requis."),
        new("Gmail", "imap.gmail.com", 993, true, "Mot de passe d'application requis."),
        new("Outlook / Hotmail", "outlook.office365.com", 993, true, "Les comptes personnels nécessitent OAuth2, non supporté en v1."),
        new("Yahoo", "imap.mail.yahoo.com", 993, true, "Mot de passe d'application requis."),
        new("IMAP personnalisé", "", 993, true, "Saisir l'hôte et le port manuellement."),
    ];
}
