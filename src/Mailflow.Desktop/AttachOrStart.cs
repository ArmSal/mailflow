namespace Mailflow.Desktop;

public enum ServerMode { Attach, Start }

public static class AttachOrStart
{
    /// <summary>Attach when the probe says a Mailflow server already answers; otherwise start one in-process.</summary>
    public static async Task<ServerMode> DecideAsync(Func<CancellationToken, Task<string?>> probe, CancellationToken ct = default)
    {
        try { return await probe(ct) == "Mailflow" ? ServerMode.Attach : ServerMode.Start; }
        catch (Exception) { return ServerMode.Start; }
    }

    public static async Task<string?> HttpProbeAsync(Uri baseUrl, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        return (await http.GetStringAsync(new Uri(baseUrl, "/healthz"), ct)).Trim();
    }
}
