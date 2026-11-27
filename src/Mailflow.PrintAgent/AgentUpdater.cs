using System.Diagnostics;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using Mailflow.Application.Updates;

namespace Mailflow.Agent;

/// <summary>
/// Once a day asks GitHub for the latest release. "Notify" (default) only writes it to agent.log; "Auto" downloads the signed
/// installer, verifies it and runs it silently between two print jobs. Same signature check as Mailflow itself.
/// </summary>
internal sealed class AgentUpdater(HttpClient http, string mode, Action<string> log)
{
    public const string LatestUrl = "https://api.github.com/repos/ArmSal/mailflow/releases/latest";
    private static readonly string AttemptFile = Path.Combine(AppContext.BaseDirectory, "update-attempt.txt");
    private static readonly string WorkDir = Path.Combine(AppContext.BaseDirectory, "updates");

    private sealed record Dto([property: JsonPropertyName("tag_name")] string? Tag);

    public static Version Current => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);

    public static bool IsAuto(string? mode) => string.Equals(mode?.Trim(), "Auto", StringComparison.OrdinalIgnoreCase);

    /// <summary>Install only in Auto mode, only for a newer version, and never twice for a version that already failed once.</summary>
    public static bool ShouldInstall(string? mode, string tag, Version current, string? lastAttempt)
        => IsAuto(mode)
           && Version.TryParse(tag.TrimStart('v'), out var v) && v > new Version(current.Major, current.Minor, Math.Max(current.Build, 0))
           && !string.Equals(lastAttempt?.Trim(), tag, StringComparison.Ordinal);

    /// <summary>True when the installer was started (the agent is about to be stopped by it).</summary>
    public async Task<bool> CheckAsync(CancellationToken ct)
    {
        string? tag;
        try { tag = (await http.GetFromJsonAsync<Dto>(LatestUrl, ct))?.Tag; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { return false; }
        if (tag is null || !Version.TryParse(tag.TrimStart('v'), out var latest) || latest <= new Version(Current.Major, Current.Minor, Math.Max(Current.Build, 0))) return false;

        if (!IsAuto(mode)) { log($"Nouvelle version disponible : {tag} (UpdateMode = Notify : rien n'est installé)."); return false; }
        var last = File.Exists(AttemptFile) ? File.ReadAllText(AttemptFile) : null;
        if (!ShouldInstall(mode, tag, Current, last)) return false;

        var r = await UpdateDownloader.PrepareAsync(http, UpdateProduct.PrintAgent, tag, Current, WorkDir, ct);
        if (r.Error != PrepareError.None) { log($"Mise à jour {tag} refusée ou impossible : {r.Error}."); return false; }

        File.WriteAllText(AttemptFile, tag);
        log($"Installation de la version {tag}.");
        // Setup stops this agent, replaces the program (agent.json is kept) and starts the new one.
        var installDir = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        Process.Start(new ProcessStartInfo(r.InstallerPath!, "/S \"/dir=" + installDir + "\"") { UseShellExecute = false })?.Dispose();
        return true;
    }
}
