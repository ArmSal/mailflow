using Mailflow.Domain.Entities;

namespace Mailflow.Application.Processing;

public static class PrintPolicy
{
    public const int DefaultMaxAgeHours = 24;

    public static bool Allows(bool ruleSaysPrint, string fromAddress, IEnumerable<AllowedSender> allowedSenders, int mailboxStoreId,
        DateTime receivedAt, DateTime now, int maxAgeHours, Printer? printer)
    {
        if (!ruleSaysPrint || printer is not { IsActive: true }) return false;
        if (now - receivedAt > TimeSpan.FromHours(maxAgeHours)) return false;
        return allowedSenders.Any(s => (s.StoreId is null || s.StoreId == mailboxStoreId) && SenderMatches(s.Pattern, fromAddress));
    }

    static bool SenderMatches(string pattern, string fromAddress)
    {
        var p = pattern.Trim().TrimStart('@');
        if (p.Length == 0) return false;
        if (p.Contains('@')) return string.Equals(p, fromAddress, StringComparison.OrdinalIgnoreCase);
        var at = fromAddress.LastIndexOf('@');
        return at >= 0 && string.Equals(p, fromAddress[(at + 1)..], StringComparison.OrdinalIgnoreCase);
    }
}
