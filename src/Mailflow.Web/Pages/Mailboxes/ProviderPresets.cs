using Mailflow.Infrastructure.Mail;

namespace Mailflow.Web.Pages.Mailboxes;

public sealed record ProviderPreset(string Key, string Label, string Host, int Port, bool UseSsl, string Domains);

/// <summary>View of <see cref="ImapPresets"/> for the mailbox form (add providers there, not here).</summary>
public static class ProviderPresets
{
    public static readonly IReadOnlyList<ProviderPreset> All =
        ImapPresets.All.Select(p => new ProviderPreset(p.Name, p.Name, p.Host, p.Port, p.UseSsl, string.Join(",", p.Domains ?? []))).ToList();
}
