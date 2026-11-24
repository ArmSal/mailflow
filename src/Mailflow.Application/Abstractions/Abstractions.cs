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
    IReadOnlyList<MailAttachment> Pdfs);

/// <summary>When <paramref name="AccessToken"/> is set the client signs in with XOAUTH2 and ignores the password.</summary>
public sealed record MailboxConnection(string Host, int Port, bool UseSsl, string Username, string Password, string? AccessToken = null);

/// <summary>Builds the connection for a stored mailbox: decrypts the password, or silently obtains an OAuth access token.</summary>
public interface IMailboxConnectionFactory
{
    /// <exception cref="OAuthReconnectRequiredException">The mailbox uses OAuth and the user must sign in again.</exception>
    Task<MailboxConnection> CreateAsync(Mailbox mailbox, CancellationToken ct);
}

public sealed class OAuthReconnectRequiredException(string message) : Exception(message);

public sealed record DeviceCodeInfo(string UserCode, string VerificationUrl, DateTime ExpiresAtUtc);

public enum OAuthSignInState { None, WaitingForUser, Succeeded, Failed }

public sealed record OAuthSignInStatus(OAuthSignInState State, DeviceCodeInfo? Code = null, string? Error = null);

/// <summary>Microsoft device-code sign-in (works headless, e.g. when Mailflow runs as a Windows service).</summary>
public interface IOAuthSignInService
{
    /// <summary>True when the Azure application (client) id has been configured in the settings.</summary>
    Task<bool> IsConfiguredAsync(CancellationToken ct);

    /// <summary>Starts a sign-in for the mailbox and returns once the code to show the user is known. Polling continues in the background.</summary>
    Task<OAuthSignInStatus> StartAsync(int mailboxId, CancellationToken ct);

    OAuthSignInStatus GetStatus(int mailboxId);

    /// <summary>True when a token cache is stored for the mailbox (it may still need renewing).</summary>
    bool IsSignedIn(Mailbox mailbox);

    /// <summary>Forgets the stored tokens of the mailbox.</summary>
    Task SignOutAsync(int mailboxId, CancellationToken ct);
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
    DbSet<Store> Stores { get; }
    DbSet<Mailbox> Mailboxes { get; }
    DbSet<Printer> Printers { get; }
    DbSet<Category> Categories { get; }
    DbSet<Rule> Rules { get; }
    DbSet<RuleCondition> RuleConditions { get; }
    DbSet<RuleAction> RuleActions { get; }
    DbSet<AllowedSender> AllowedSenders { get; }
    DbSet<ProcessedMessage> ProcessedMessages { get; }
    DbSet<Document> Documents { get; }
    DbSet<RunLog> RunLogs { get; }
    DbSet<Setting> Settings { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
