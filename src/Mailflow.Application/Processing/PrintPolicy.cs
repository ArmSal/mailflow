using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;

namespace Mailflow.Application.Processing;

public static class PrintPolicy
{
    public const int DefaultMaxAgeHours = 24;

    public static bool Allows(bool ruleSaysPrint, DateTime receivedAt, DateTime now, int maxAgeHours, Printer? printer) =>
        ruleSaysPrint && ReasonBlocked(ruleSaysPrint, receivedAt, now, maxAgeHours, printer) is null;

    /// <summary>Returns a French user-facing reason when automatic printing is blocked by policy, else null.</summary>
    public static string? ReasonBlocked(bool ruleSaysPrint, DateTime receivedAt, DateTime now, int maxAgeHours, Printer? printer)
    {
        if (!ruleSaysPrint) return null;
        if (printer is not { IsActive: true }) return "Impression bloquée : imprimante inactive ou introuvable.";
        if (now - receivedAt > TimeSpan.FromHours(maxAgeHours)) return $"Impression bloquée : e-mail trop ancien (> {maxAgeHours} h).";
        return null;
    }

    public const string SenderBlockedMessage = "Impression bloquée : expéditeur non vérifié (DKIM/DMARC).";

    /// <summary>Missing or invalid text means Warn.</summary>
    public static SenderCheckMode ParseMode(string? value) =>
        Enum.TryParse<SenderCheckMode>(value?.Trim(), true, out var m) && Enum.IsDefined(m) ? m : SenderCheckMode.Warn;

    /// <summary>Strict blocks printing unless the verdict is a Pass that came from the mailbox's trusted server id; Off and Warn never block.</summary>
    public static bool SenderBlocksPrint(SenderCheckMode mode, SenderVerification verification, bool trustedSource) =>
        mode == SenderCheckMode.Strict && (verification != SenderVerification.Pass || !trustedSource);
}
