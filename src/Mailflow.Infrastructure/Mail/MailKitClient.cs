using System.Net;
using System.Text.RegularExpressions;
using Mailflow.Application.Abstractions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace Mailflow.Infrastructure.Mail;

public sealed partial class MailKitClient : IMailClient
{
    private const int ImapTimeoutMs = 30_000;

    public async Task<ConnectionTestResult> TestConnectionAsync(MailboxConnection c, CancellationToken ct)
    {
        try
        {
            using var client = await ConnectAsync(c, ct);
            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadOnly, ct);
            var count = inbox.Count;
            var folders = new List<string>();
            if (client.PersonalNamespaces.Count > 0)
                foreach (var f in await client.GetFoldersAsync(client.PersonalNamespaces[0], cancellationToken: ct))
                    folders.Add(f.FullName);
            if (!folders.Contains(inbox.FullName)) folders.Insert(0, inbox.FullName);
            await client.DisconnectAsync(true, ct);
            return new ConnectionTestResult(true, count, folders, null);
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, 0, [], ex.Message);
        }
    }

    public async Task<IReadOnlyList<MailMessage>> FetchSinceAsync(MailboxConnection c, string folder, DateTime since, CancellationToken ct)
    {
        using var client = await ConnectAsync(c, ct);
        var box = await client.GetFolderAsync(folder, ct);
        // Read-only: nothing fetched here can alter flags. GetMessage uses BODY.PEEK.
        await box.OpenAsync(FolderAccess.ReadOnly, ct);
        var uids = await box.SearchAsync(SearchQuery.DeliveredAfter(since.Date), ct);
        var result = new List<MailMessage>();
        foreach (var uid in uids)
        {
            var m = await box.GetMessageAsync(uid, ct);
            var pdfs = new List<MailAttachment>();
            foreach (var part in m.Attachments.OfType<MimePart>())
            {
                var name = part.FileName ?? part.ContentType.Name ?? "document.pdf";
                var isPdf = part.ContentType.IsMimeType("application", "pdf")
                    || name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
                if (!isPdf || part.Content is null) continue;
                using var ms = new MemoryStream();
                await part.Content.DecodeToAsync(ms, ct);
                pdfs.Add(new MailAttachment(name, ms.ToArray()));
            }
            var authHeaders = m.Headers.Where(h => h.Id == HeaderId.AuthenticationResults).Select(h => h.Value).ToList();
            var fromAddress = m.From.Mailboxes.FirstOrDefault()?.Address ?? "";
            result.Add(new MailMessage(
                m.MessageId ?? $"uid-{uid.Id}",
                uid.Id,
                m.Date.UtcDateTime,
                fromAddress,
                m.To.Mailboxes.FirstOrDefault()?.Address ?? "",
                m.Subject ?? "",
                ExtractBody(m.TextBody, m.HtmlBody),
                pdfs,
                SenderAuthParser.Parse(authHeaders, fromAddress, c.TrustedAuthservId),
                !string.IsNullOrWhiteSpace(c.TrustedAuthservId)));
        }
        await client.DisconnectAsync(true, ct);
        return result;
    }

    public async Task MarkAsReadAsync(MailboxConnection c, string folder, uint uid, CancellationToken ct)
    {
        using var client = await ConnectAsync(c, ct);
        var box = await client.GetFolderAsync(folder, ct);
        await box.OpenAsync(FolderAccess.ReadWrite, ct);
        await box.AddFlagsAsync(new UniqueId(uid), MessageFlags.Seen, true, ct);
        await client.DisconnectAsync(true, ct);
    }

    public async Task MoveAsync(MailboxConnection c, string folder, uint uid, string targetFolder, CancellationToken ct)
    {
        using var client = await ConnectAsync(c, ct);
        var box = await client.GetFolderAsync(folder, ct);
        await box.OpenAsync(FolderAccess.ReadWrite, ct);
        var target = await GetOrCreateFolderAsync(client, targetFolder, ct);
        var id = new UniqueId(uid);
        if (client.Capabilities.HasFlag(ImapCapabilities.Move))
        {
            await box.MoveToAsync(id, target, ct);
        }
        else
        {
            // Only the copied message is flagged and expunged (UID EXPUNGE); never a plain EXPUNGE.
            await box.CopyToAsync(id, target, ct);
            await box.AddFlagsAsync(id, MessageFlags.Deleted, true, ct);
            if (client.Capabilities.HasFlag(ImapCapabilities.UidPlus))
                await box.ExpungeAsync([id], ct);
        }
        await client.DisconnectAsync(true, ct);
    }

    private static async Task<IMailFolder> GetOrCreateFolderAsync(ImapClient client, string name, CancellationToken ct)
    {
        try { return await client.GetFolderAsync(name, ct); }
        catch (FolderNotFoundException)
        {
            var root = client.PersonalNamespaces.Count > 0
                ? await client.GetFolderAsync(client.PersonalNamespaces[0].Path, ct)
                : client.Inbox.ParentFolder ?? throw new InvalidOperationException("No root folder.");
            return await root.CreateAsync(name, true, ct);
        }
    }

    /// <summary>TLS is always required, except plaintext to a loopback host (local test servers).</summary>
    public static SecureSocketOptions SocketOptionsFor(MailboxConnection c)
    {
        if (c.UseSsl) return SecureSocketOptions.SslOnConnect;
        var loopback = c.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || (System.Net.IPAddress.TryParse(c.Host, out var ip) && System.Net.IPAddress.IsLoopback(ip));
        return loopback ? SecureSocketOptions.None : SecureSocketOptions.StartTls;
    }

    private static async Task<ImapClient> ConnectAsync(MailboxConnection c, CancellationToken ct)
    {
        var client = new ImapClient { Timeout = ImapTimeoutMs };
        try
        {
            await client.ConnectAsync(c.Host, c.Port,
                SocketOptionsFor(c), ct);
            await client.AuthenticateAsync(c.Username, c.Password, ct);
            return client;
        }
        catch { client.Dispose(); throw; }
    }

    /// <summary>HTML beyond this size is not text-extracted: real e-mail bodies are far smaller, hostile ones must stay cheap.</summary>
    private const int MaxHtmlChars = 200_000;
    private const int RegexTimeoutMs = 1000;

    internal static string ExtractBody(string? textBody, string? htmlBody)
    {
        if (!string.IsNullOrWhiteSpace(textBody)) return textBody;
        if (string.IsNullOrWhiteSpace(htmlBody)) return "";
        if (htmlBody.Length > MaxHtmlChars) htmlBody = htmlBody[..MaxHtmlChars];
        try
        {
            var stripped = StyleOrScriptRegex().Replace(htmlBody, " ");
            stripped = BlockTagRegex().Replace(stripped, "\n");
            stripped = AnyTagRegex().Replace(stripped, " ");
            stripped = WebUtility.HtmlDecode(stripped);
            stripped = MultiBlankLinesRegex().Replace(stripped, "\n\n");
            return stripped.Trim();
        }
        catch (RegexMatchTimeoutException)
        {
            return "";
        }
    }

    [GeneratedRegex(@"<(style|script)\b[^>]*>[\s\S]*?</\1>", RegexOptions.IgnoreCase, RegexTimeoutMs)]
    private static partial Regex StyleOrScriptRegex();

    [GeneratedRegex(@"<(br\s*/?|/(p|div|li|tr|h[1-6]))\s*>", RegexOptions.IgnoreCase, RegexTimeoutMs)]
    private static partial Regex BlockTagRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.None, RegexTimeoutMs)]
    private static partial Regex AnyTagRegex();

    [GeneratedRegex(@"\n{3,}", RegexOptions.None, RegexTimeoutMs)]
    private static partial Regex MultiBlankLinesRegex();
}

