using System.Net.Security;
using System.Security.Cryptography;

namespace Mailflow.Agent;

/// <param name="Fingerprint">SHA-256 of the certificate the server presented (upper-case hex), or null if no TLS connection was made.</param>
/// <param name="TrustedByWindows">True when Windows would accept the certificate without a pin (a real certificate for that address).</param>
public sealed record CertificateProbeResult(string? Fingerprint, bool TrustedByWindows, string? Error);

/// <summary>Looks at the certificate a server presents, to let the user confirm its fingerprint once during setup.</summary>
public static class CertificateProbe
{
    public static async Task<CertificateProbeResult> RunAsync(Uri baseAddress, CancellationToken ct = default)
    {
        string? fingerprint = null;
        var trusted = false;
        var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(10) };
        handler.SslOptions.RemoteCertificateValidationCallback = (_, cert, _, errors) =>
        {
            if (cert is not null) fingerprint = Convert.ToHexString(cert.GetCertHash(HashAlgorithmName.SHA256));
            trusted = errors == SslPolicyErrors.None;
            return true; // only looking: nothing is sent that could reveal a secret (no name, no password)
        };
        try
        {
            using var http = new HttpClient(handler) { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(15) };
            using var _ = await http.GetAsync("api/agent/ping", ct);
            return new CertificateProbeResult(fingerprint, trusted, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new CertificateProbeResult(fingerprint, trusted, fingerprint is null ? ex.Message : null);
        }
    }
}
