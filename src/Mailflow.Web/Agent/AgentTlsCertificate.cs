using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mailflow.Application.Abstractions;

namespace Mailflow.Web.Agent;

/// <summary>What the Print agents page shows about the agent listener.</summary>
/// <param name="Https">The agent listener is an https:// address.</param>
/// <param name="Fingerprint">SHA-256 of the certificate Mailflow generated, or null (plain http, or the administrator's own certificate).</param>
public sealed record AgentListenerInfo(bool Https, string? Fingerprint);

/// <summary>
/// The self-signed certificate of the agent listener. It is created on first use, stored encrypted for the Windows
/// user running Mailflow, and reused after a restart. Agents do not trust it through Windows but by its fingerprint,
/// which the administrator compares once (shown on the Print agents page).
/// </summary>
public static class AgentTlsCertificate
{
    public static X509Certificate2 LoadOrCreate(string path, ISecretStore secrets)
    {
        if (File.Exists(path))
        {
            try { return Load(Convert.FromBase64String(secrets.Unprotect(File.ReadAllText(path).Trim()))); }
            catch (Exception ex) when (ex is CryptographicException or FormatException or IOException)
            {
                // Unreadable (for example copied from another Windows account): make a new one; agents then need the new fingerprint.
            }
        }

        var pfx = Create();
        File.WriteAllText(path, secrets.Protect(Convert.ToBase64String(pfx)));
        return Load(pfx);
    }

    /// <summary>SHA-256 of the certificate, 64 upper-case hex characters.</summary>
    public static string Fingerprint(X509Certificate2 cert) => Convert.ToHexString(cert.GetCertHash(HashAlgorithmName.SHA256));

    /// <summary>"AB:CD:..." for reading and comparing by eye.</summary>
    public static string Format(string fingerprint) =>
        string.Join(":", Enumerable.Range(0, fingerprint.Length / 2).Select(i => fingerprint.Substring(i * 2, 2)));

    /// <remarks>Schannel cannot serve a certificate whose key lives only in memory, so the key goes to the user profile; Windows removes it when the certificate is disposed.</remarks>
    private static X509Certificate2 Load(byte[] pfx) => X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.DefaultKeySet);

    private static byte[] Create()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Mailflow agent listener", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddDnsName(Dns.GetHostName());
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false)); // server authentication
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        return cert.Export(X509ContentType.Pfx);
    }
}
