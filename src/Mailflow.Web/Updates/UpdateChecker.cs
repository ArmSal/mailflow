using Mailflow.Application.Updates;
using Mailflow.Domain.Entities;
using Mailflow.Web.Worker;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using Mailflow.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Web.Updates;

public sealed record ReleaseInfo(string Tag, string Url);

/// <summary>Notify: banner only. Manual: the banner also has an install button. Auto: installs by itself when Mailflow is idle.</summary>
public enum UpdateMode { Notify, Manual, Auto }

/// <summary>Fetches the latest published release. Returns null when it cannot be read.</summary>
public interface IReleaseSource
{
    Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct);
}

/// <summary>GitHub Releases API. Anonymous, generic User-Agent, no data about the installation is sent.</summary>
public sealed class GitHubReleaseSource(HttpClient http) : IReleaseSource
{
    public const string LatestUrl = "https://api.github.com/repos/ArmSal/mailflow/releases/latest";

    private sealed record Dto([property: JsonPropertyName("tag_name")] string? Tag, [property: JsonPropertyName("html_url")] string? Url);

    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct)
    {
        try
        {
            var dto = await http.GetFromJsonAsync<Dto>(LatestUrl, ct);
            return dto?.Tag is { Length: > 0 } t && dto.Url is { Length: > 0 } u && u.StartsWith("https://", StringComparison.Ordinal) ? new ReleaseInfo(t, u) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or NotSupportedException)
        {
            return null;
        }
    }
}

/// <summary>Holds the newest release found, if it is newer than the running version.</summary>
public sealed class UpdateChecker(IReleaseSource source)
{
    public const string SettingKey = "UpdateCheck";

    /// <summary>On unless explicitly set to "false".</summary>
    public static bool ParseEnabled(string? value)
        => !string.Equals(value?.Trim(), "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>Setting holding the <see cref="UpdateMode"/>; Notify when missing or unknown.</summary>
    public const string ModeKey = "UpdateMode";

    public static UpdateMode ParseMode(string? value)
        => Enum.TryParse<UpdateMode>(value?.Trim(), true, out var m) && Enum.IsDefined(m) ? m : UpdateMode.Notify;

    public UpdateMode Mode { get; set; }

    /// <summary>True when the banner offers the install button.</summary>
    public bool InstallEnabled => Mode != UpdateMode.Notify;

    /// <summary>Version of the last install that did not complete, shown as a warning until the next restart.</summary>
    public string? FailedTag { get; set; }

    /// <summary>Called when an installer was started: if this process is still alive after 5 minutes, Setup did not replace it.</summary>
    public void WatchAttempt(string tag)
        => _ = Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(_ => FailedTag = tag, TaskScheduler.Default);

    /// <summary>The release Auto mode should install now, or null. A version already attempted once is never retried automatically.</summary>
    public static ReleaseInfo? AutoCandidate(UpdateMode mode, ReleaseInfo? available, bool setupInstall, string? lastAttemptTag)
        => mode == UpdateMode.Auto && setupInstall && available is not null && available.Tag != lastAttemptTag ? available : null;

    public static Version Current { get; } = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);

    public ReleaseInfo? Available { get; private set; }

    public static bool IsNewer(string tag, Version current)
        => Version.TryParse(tag.TrimStart('v', 'V').Split('-', '+')[0], out var v) && Normalize(v) > Normalize(current);

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    public async Task CheckAsync(bool enabled, Version current, CancellationToken ct)
    {
        if (!enabled) { Available = null; return; }
        var latest = await source.GetLatestAsync(ct);
        if (latest is not null) Available = IsNewer(latest.Tag, current) ? latest : null;
    }
}


/// <summary>Checks at startup, then at most once a day; in Auto mode also installs when Mailflow is idle.</summary>
public sealed class UpdateCheckService(IServiceScopeFactory scopes, UpdateChecker checker, WorkerState worker, ILogger<UpdateCheckService> logger) : BackgroundService
{
    private static readonly TimeSpan IdleRetry = TimeSpan.FromMinutes(10);
    private const int IdleAttempts = 6;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        checker.FailedTag = UpdateInstaller.Reconcile(UpdateChecker.Current);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (var scope = scopes.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
                    var value = await db.Settings.Where(s => s.Key == UpdateChecker.SettingKey).Select(s => s.Value).FirstOrDefaultAsync(stoppingToken);
                    var mode = await db.Settings.Where(s => s.Key == UpdateChecker.ModeKey).Select(s => s.Value).FirstOrDefaultAsync(stoppingToken);
                    checker.Mode = UpdateChecker.ParseMode(mode);
                    await checker.CheckAsync(UpdateChecker.ParseEnabled(value), UpdateChecker.Current, stoppingToken);
                }
                if (await TryAutoInstallAsync(stoppingToken)) return;   // Setup is about to stop us
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogDebug(ex, "Update check failed"); }
            try { await Task.Delay(TimeSpan.FromHours(24), stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Auto mode: waits for Mailflow to be idle (no mailbox run, no print job in flight), then installs. True when Setup was started.</summary>
    private async Task<bool> TryAutoInstallAsync(CancellationToken ct)
    {
        for (var i = 0; i < IdleAttempts; i++)
        {
            var release = UpdateChecker.AutoCandidate(checker.Mode, checker.Available, UpdateInstaller.IsSetupInstall(), UpdateInstaller.ReadAttempt());
            if (release is null) return false;
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var busy = worker.IsRunning || await db.PrintJobs.AnyAsync(j => j.Status == PrintJobStatus.Pending || j.Status == PrintJobStatus.Sent, ct);
            if (busy) { await Task.Delay(IdleRetry, ct); continue; }

            var installer = scope.ServiceProvider.GetRequiredService<UpdateInstaller>();
            var r = await installer.PrepareAsync(release.Tag, ct);
            if (r.Error != PrepareError.None) { logger.LogWarning("Automatic update to {Tag} not installed: {Error}", release.Tag, r.Error); return false; }
            logger.LogInformation("Installing {Tag}", release.Tag);
            installer.Launch(r.InstallerPath!, release.Tag);
            return true;
        }
        return false;
    }
}
