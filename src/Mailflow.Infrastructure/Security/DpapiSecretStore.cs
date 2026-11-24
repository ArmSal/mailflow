using System.Security.Cryptography;
using System.Text;
using Mailflow.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Mailflow.Infrastructure.Security;

public sealed class DpapiOptions
{
    public DataProtectionScope Scope { get; set; } = DataProtectionScope.CurrentUser;
}

public sealed class DpapiSecretStore(IOptions<DpapiOptions> options) : ISecretStore
{
    public string Protect(string plainText) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plainText), null, options.Value.Scope));

    public string Unprotect(string protectedText) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedText), null, options.Value.Scope));
}
