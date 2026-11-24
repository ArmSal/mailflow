namespace Mailflow.Web.Pages.Mailboxes;

public sealed record ProviderPreset(string Key, string Label, string Host, int Port, bool UseSsl);

/// <summary>View-model copy of the provider presets (the real ones live in Infrastructure).</summary>
public static class ProviderPresets
{
    public static readonly IReadOnlyList<ProviderPreset> All =
    [
        new("ovh-mx", "OVH MX Plan", "ssl0.ovh.net", 993, true),
        new("ovh-pro", "OVH Email Pro", "pro1.mail.ovh.net", 993, true),
        new("icloud", "iCloud", "imap.mail.me.com", 993, true),
        new("gmail", "Gmail", "imap.gmail.com", 993, true),
        new("outlook", "Outlook / Hotmail", "outlook.office365.com", 993, true),
        new("yahoo", "Yahoo", "imap.mail.yahoo.com", 993, true),
        new("custom", "IMAP personnalisé", "", 993, true),
    ];
}
