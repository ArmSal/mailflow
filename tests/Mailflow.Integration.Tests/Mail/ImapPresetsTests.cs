using Mailflow.Infrastructure.Mail;

namespace Mailflow.Integration.Tests.Mail;

public class ImapPresetsTests
{
    [Theory]
    [InlineData("OVH MX Plan", "ssl0.ovh.net")]
    [InlineData("OVH Email Pro", "pro1.mail.ovh.net")]
    [InlineData("iCloud", "imap.mail.me.com")]
    [InlineData("Gmail", "imap.gmail.com")]
    [InlineData("Outlook / Hotmail", "outlook.office365.com")]
    [InlineData("Yahoo", "imap.mail.yahoo.com")]
    public void Preset_has_expected_host(string name, string host)
    {
        var p = Assert.Single(ImapPresets.All, x => x.Name == name);
        Assert.Equal(host, p.Host);
        Assert.Equal(993, p.Port);
        Assert.True(p.UseSsl);
    }

    [Fact]
    public void Custom_preset_has_empty_host()
    {
        var p = Assert.Single(ImapPresets.All, x => x.Name == "IMAP personnalise");
        Assert.Equal("", p.Host);
    }

    [Fact]
    public async Task TestConnection_never_throws()
    {
        var r = await new MailKitClient().TestConnectionAsync(
            new(" invalid host", 1, true, "u", "p"), CancellationToken.None);
        Assert.False(r.Success);
        Assert.False(string.IsNullOrEmpty(r.Error));
    }
}
