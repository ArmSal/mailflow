using Mailflow.Application.Abstractions;
using Mailflow.Infrastructure.Mail;
using MailKit.Security;
using Xunit;

namespace Mailflow.Integration.Tests.Mail;

public class TlsPolicyTests
{
    private static MailboxConnection Conn(string host, bool ssl) => new(host, 143, ssl, "u", "p");

    [Fact]
    public void Ssl_connects_with_ssl_on_connect() =>
        Assert.Equal(SecureSocketOptions.SslOnConnect, MailKitClient.SocketOptionsFor(Conn("imap.example.com", true)));

    [Fact]
    public void Remote_host_without_ssl_requires_starttls_never_cleartext() =>
        Assert.Equal(SecureSocketOptions.StartTls, MailKitClient.SocketOptionsFor(Conn("imap.example.com", false)));

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void Loopback_host_may_use_plaintext_for_local_test_servers(string host) =>
        Assert.Equal(SecureSocketOptions.None, MailKitClient.SocketOptionsFor(Conn(host, false)));
}
