using Mailflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Application.Abstractions;

public sealed record MailAttachment(string FileName, byte[] Content);

public sealed record MailMessage(
    string MessageId,
    uint Uid,
    DateTime ReceivedAt,
    string From,
    string To,
    string Subject,
    string Body,
    IReadOnlyList<MailAttachment> Pdfs,
    Mailflow.Domain.Enums.SenderVerification SenderVerification = Mailflow.Domain.Enums.SenderVerification.Unknown,
    /// <summary>True when the verdict was read under the mailbox's trusted authserv-id (only then can Strict mode rely on a Pass).</summary>
    bool TrustedSource = false);

/// <summary>Everything the IMAP client needs to connect to a mailbox.</summary>
public sealed record MailboxConnection(string Host, int Port, bool UseSsl, string Username, string Password, string? TrustedAuthservId = null);

/// <summary>Builds the connection for a stored mailbox (decrypts the password).</summary>
public interface IMailboxConnectionFactory
{
    Task<MailboxConnection> CreateAsync(Mailbox mailbox, CancellationToken ct);
}

public sealed record ConnectionTestResult(bool Success, int MessageCount, IReadOnlyList<string> Folders, string? Error);

public interface IMailClient
{
    Task<ConnectionTestResult> TestConnectionAsync(MailboxConnection connection, CancellationToken ct);
    Task<IReadOnlyList<MailMessage>> FetchSinceAsync(MailboxConnection connection, string folder, DateTime since, CancellationToken ct);
    Task MarkAsReadAsync(MailboxConnection connection, string folder, uint uid, CancellationToken ct);
    Task MoveAsync(MailboxConnection connection, string folder, uint uid, string targetFolder, CancellationToken ct);
}

public interface IPdfTextReader
{
    /// <summary>Text of the first <paramref name="maxPages"/> pages.</summary>
    string ReadText(byte[] pdf, int maxPages = 2);
}

public interface IEmailPdfRenderer
{
    /// <summary>Renders an email (headers and body) as a printable PDF when no PDF attachment exists.</summary>
    byte[] Render(MailMessage message);
}

public interface IPrinterService
{
    IReadOnlyList<string> GetInstalledPrinters();
    Task PrintAsync(string pdfPath, string printerName, int copies, bool duplex, CancellationToken ct);
}

public sealed record StoredDocument(string Path, string Sha256, bool WasDuplicate);

public interface IDocumentStore
{
    Task<StoredDocument> SaveAsync(byte[] content, string categorySubfolder, string supplier, string originalName, DateTime receivedAt, CancellationToken ct);
    /// <summary>Returns the new path (it can differ from the requested one after a name collision).</summary>
    Task<string> MoveAsync(string storedPath, string newCategorySubfolder, CancellationToken ct);
}

public interface ISecretStore
{
    string Protect(string plainText);
    string Unprotect(string protectedText);
}

public interface IClock
{
    DateTime UtcNow { get; }
}

public interface IAppDbContext
{
    DbSet<Mailbox> Mailboxes { get; }
    DbSet<Printer> Printers { get; }
    DbSet<Category> Categories { get; }
    DbSet<Rule> Rules { get; }
    DbSet<RuleCondition> RuleConditions { get; }
    DbSet<RuleAction> RuleActions { get; }
    DbSet<ProcessedMessage> ProcessedMessages { get; }
    DbSet<Document> Documents { get; }
    DbSet<RunLog> RunLogs { get; }
    DbSet<Setting> Settings { get; }
    DbSet<PrintAgent> PrintAgents { get; }
    DbSet<PrintJob> PrintJobs { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
