namespace Mailflow.Desktop;

public enum ServerMode { Attach, Start, Refuse }

public static class AttachOrStart
{
    public const string RefuseMessageFormat =
        "Le port {0} est utilisé par un autre programme que Mailflow. Fermez ce programme ou changez le port dans appsettings.json (Kestrel). " +
        "Mailflow ne s'ouvrira pas pour protéger votre mot de passe.";

    /// <summary>
    /// Attach only when the probe says "Mailflow" AND the listening socket belongs to this very executable. A banner alone is not
    /// proof: another local program could squat the port and imitate it to show a fake login page.
    /// A copy of Mailflow in a different folder is treated as a foreign owner on purpose (we cannot tell it from an impostor).
    /// Nothing answers the banner: start our own server.
    /// </summary>
    public static async Task<ServerMode> DecideAsync(Func<CancellationToken, Task<string?>> probe, Func<string?> ownerImagePath,
        string? currentExePath, CancellationToken ct = default)
    {
        string? banner;
        try { banner = await probe(ct); }
        catch (Exception) { return ServerMode.Start; }
        if (banner != "Mailflow") return ServerMode.Start;

        string? owner;
        try { owner = ownerImagePath(); }
        catch (Exception) { owner = null; }
        return SamePath(owner, currentExePath) ? ServerMode.Attach : ServerMode.Refuse;
    }

    public static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try
        {
            static string Norm(string p) => Path.GetFullPath(p.Trim()).TrimEnd('\\', '/');
            return string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { return false; }
    }

    public static async Task<string?> HttpProbeAsync(Uri baseUrl, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        return (await http.GetStringAsync(new Uri(baseUrl, "/healthz"), ct)).Trim();
    }
}
