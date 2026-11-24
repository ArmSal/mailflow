namespace Mailflow.Infrastructure.Mail;

public sealed record ImapPreset(string Name, string Host, int Port, bool UseSsl, string Note);

public static class ImapPresets
{
    public static IReadOnlyList<ImapPreset> All { get; } =
    [
        new("OVH MX Plan", "ssl0.ovh.net", 993, true, "Mot de passe de la boite mail."),
        new("OVH Email Pro", "pro1.mail.ovh.net", 993, true, "Mot de passe de la boite mail."),
        new("iCloud", "imap.mail.me.com", 993, true, "Mot de passe specifique a l'application requis."),
        new("Gmail", "imap.gmail.com", 993, true, "Mot de passe d'application requis."),
        new("Outlook / Hotmail", "outlook.office365.com", 993, true, "Les comptes personnels necessitent OAuth2, non supporte en v1."),
        new("Yahoo", "imap.mail.yahoo.com", 993, true, "Mot de passe d'application requis."),
        new("IMAP personnalise", "", 993, true, "Saisir l'hote et le port manuellement."),
    ];
}
