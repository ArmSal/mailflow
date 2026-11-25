using Mailflow.Web;
using Microsoft.AspNetCore.Http;

namespace Mailflow.Integration.Tests.Pages;

public class FirstRunTests
{
    [Theory]
    [InlineData("/Setup")]
    [InlineData("/language/en")]
    [InlineData("/language/fr")]
    [InlineData("/healthz")]
    [InlineData("/css/base.css")]
    [InlineData("/lib/htmx/htmx.min.js")]
    public void Reachable_before_the_admin_password_exists(string path) =>
        Assert.True(MailflowHost.IsAvailableBeforeSetup(new PathString(path)));

    [Theory]
    [InlineData("/")]
    [InlineData("/Settings")]
    [InlineData("/Help")]
    public void Everything_else_waits_for_setup(string path) =>
        Assert.False(MailflowHost.IsAvailableBeforeSetup(new PathString(path)));
}
