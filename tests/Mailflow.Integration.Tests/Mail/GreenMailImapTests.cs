using System.Diagnostics;
using Mailflow.Application.Abstractions;
using Mailflow.Infrastructure.Mail;
using MailKit.Net.Smtp;
using MimeKit;

namespace Mailflow.Integration.Tests.Mail;

/// <summary>Starts a GreenMail container; tests are skipped when Docker is unavailable.</summary>
public sealed class GreenMailFixture : IDisposable
{
    public const int ImapPort = 3143;
    public bool Available { get; }
    private readonly string? _container;

    public GreenMailFixture()
    {
        try
        {
            _container = Run("run -d --rm -p 3143:3143 -p 3025:3025 -e \"GREENMAIL_OPTS=-Dgreenmail.setup.test.all -Dgreenmail.hostname=0.0.0.0 -Dgreenmail.auth.disabled\" greenmail/standalone:2.1.3");
            Available = _container is not null;
            if (Available) Thread.Sleep(5000);
        }
        catch { Available = false; }
    }

    private static string? Run(string args)
    {
        var psi = new ProcessStartInfo("docker", args) { RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd().Trim();
        return p.WaitForExit(120000) && p.ExitCode == 0 ? o : null;
    }

    public void Dispose() { if (_container is not null) Run($"stop {_container}"); }
}

[Trait("Category", "Docker")]
public class GreenMailImapTests(GreenMailFixture fx) : IClassFixture<GreenMailFixture>
{
    private static readonly MailboxConnection Conn = new("127.0.0.1", GreenMailFixture.ImapPort, false, "u@localhost", "pw");
    private readonly MailKitClient _client = new();

    private static async Task SendAsync()
    {
        var m = new MimeMessage();
        m.From.Add(new MailboxAddress("S", "s@localhost"));
        m.To.Add(new MailboxAddress("U", "u@localhost"));
        m.Subject = "Facture";
        var b = new BodyBuilder { TextBody = "hello" };
        b.Attachments.Add("f.pdf", "%PDF-1.4"u8.ToArray(), new ContentType("application", "pdf"));
        b.Attachments.Add("x.txt", "x"u8.ToArray(), new ContentType("text", "plain"));
        m.Body = b.ToMessageBody();
        using var smtp = new SmtpClient();
        await smtp.ConnectAsync("127.0.0.1", 3025, MailKit.Security.SecureSocketOptions.None);
        await smtp.SendAsync(m);
        await smtp.DisconnectAsync(true);
    }

    /// <summary>Reads the server-side flags of one message through a separate connection.</summary>
    private static async Task<MailKit.MessageFlags> FlagsAsync(string folder, uint uid)
    {
        using var c = new MailKit.Net.Imap.ImapClient();
        await c.ConnectAsync("127.0.0.1", GreenMailFixture.ImapPort, MailKit.Security.SecureSocketOptions.None);
        await c.AuthenticateAsync("u@localhost", "pw");
        var f = await c.GetFolderAsync(folder);
        await f.OpenAsync(MailKit.FolderAccess.ReadOnly);
        var summary = (await f.FetchAsync([new MailKit.UniqueId(uid)], new MailKit.FetchRequest(MailKit.MessageSummaryItems.Flags))).Single();
        return summary.Flags ?? MailKit.MessageFlags.None;
    }

    [SkippableFact]
    public async Task Fetch_returns_pdf_only_and_keeps_unread_then_mark_and_move()
    {
        Skip.IfNot(fx.Available, "Docker not available");
        var test = await _client.TestConnectionAsync(Conn, CancellationToken.None);
        Assert.True(test.Success, test.Error);
        await SendAsync();
        var msgs = await _client.FetchSinceAsync(Conn, "INBOX", DateTime.UtcNow.AddDays(-1), CancellationToken.None);
        var msg = Assert.Single(msgs);
        Assert.Equal("Facture", msg.Subject);
        Assert.Single(msg.Pdfs);
        Assert.False((await FlagsAsync("INBOX", msg.Uid)).HasFlag(MailKit.MessageFlags.Seen), "fetching must not mark the message as read");
        var again = await _client.FetchSinceAsync(Conn, "INBOX", DateTime.UtcNow.AddDays(-1), CancellationToken.None);
        Assert.Single(again);
        Assert.False((await FlagsAsync("INBOX", msg.Uid)).HasFlag(MailKit.MessageFlags.Seen), "a second fetch must not mark it as read either");
        await _client.MarkAsReadAsync(Conn, "INBOX", msg.Uid, CancellationToken.None);
        Assert.True((await FlagsAsync("INBOX", msg.Uid)).HasFlag(MailKit.MessageFlags.Seen), "MarkAsRead must set the Seen flag");
        await _client.MoveAsync(Conn, "INBOX", msg.Uid, "Traite", CancellationToken.None);
        Assert.Empty(await _client.FetchSinceAsync(Conn, "INBOX", DateTime.UtcNow.AddDays(-1), CancellationToken.None));
        Assert.Single(await _client.FetchSinceAsync(Conn, "Traite", DateTime.UtcNow.AddDays(-1), CancellationToken.None));
    }
}
