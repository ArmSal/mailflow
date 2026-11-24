using Mailflow.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Mailflow.Integration.Tests.Security;

public class DpapiSecretStoreTests
{
    [Fact]
    public void Round_trip()
    {
        if (!OperatingSystem.IsWindows()) return;
        var s = new DpapiSecretStore(Options.Create(new DpapiOptions()));
        var p = s.Protect("pa$$w0rd é");
        Assert.NotEqual("pa$$w0rd é", p);
        Assert.Equal("pa$$w0rd é", s.Unprotect(p));
    }
}
