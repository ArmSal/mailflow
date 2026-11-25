using Mailflow.Infrastructure.Mail;

namespace Mailflow.Integration.Tests.Mail;

public class ImapPresetsTests
{
    [Theory]
    [InlineData("OVH MX Plan", "ssl0.ovh.net")]
    [InlineData("OVH Email Pro", "pro1.mail.ovh.net")]
    [InlineData("iCloud", "imap.mail.me.com")]
    [InlineData("Gmail", "imap.gmail.com")]
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
        var p = Assert.Single(ImapPresets.All, x => x.Name == "IMAP personnalisé");
        Assert.Equal("", p.Host);
    }

    [Fact]
    public void Dashboard_dropdown_offers_exactly_the_providers_of_the_single_list()
    {
        var shown = Mailflow.Web.Pages.Mailboxes.ProviderPresets.All;
        Assert.Equal(ImapPresets.All.Select(p => (p.Name, p.Host, p.Port, p.UseSsl)),
                     shown.Select(p => (p.Label, p.Host, p.Port, p.UseSsl)));
    }

    [Theory]
    [InlineData("gmail.com", "Gmail")]
    [InlineData("icloud.com", "iCloud")]
    [InlineData("yahoo.fr", "Yahoo")]
    public void Known_email_domains_point_to_their_provider(string domain, string name) =>
        Assert.Contains(domain, Assert.Single(ImapPresets.All, x => x.Name == name).Domains!);

    [Fact]
    public void An_email_domain_belongs_to_only_one_provider() =>
        Assert.Equal(ImapPresets.All.SelectMany(p => p.Domains ?? []).Count(),
                     ImapPresets.All.SelectMany(p => p.Domains ?? []).Distinct().Count());

    [Fact]
    public async Task TestConnection_never_throws()
    {
        var r = await new MailKitClient().TestConnectionAsync(
            new(" invalid host", 1, true, "u", "p"), CancellationToken.None);
        Assert.False(r.Success);
        Assert.False(string.IsNullOrEmpty(r.Error));
    }
}

public class ExtractBodyTests
{
    [Fact]
    public void Hostile_html_cannot_stall_the_text_extraction()
    {
        var hostile = string.Concat(Enumerable.Repeat("<script>", 40_000)) + string.Concat(Enumerable.Repeat("<", 40_000));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var text = MailKitClient.ExtractBody(null, hostile);
        watch.Stop();
        Assert.NotNull(text);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), $"took {watch.Elapsed}");
    }

    [Fact]
    public void Normal_html_still_becomes_text()
    {
        var text = MailKitClient.ExtractBody(null, "<html><style>p{}</style><body><p>Bonjour</p><p>Monde &amp; co</p></body></html>");
        Assert.Contains("Bonjour", text);
        Assert.Contains("Monde & co", text);
        Assert.DoesNotContain("p{}", text);
    }
}
