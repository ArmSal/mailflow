using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using Mailflow.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Web.Updates;

public sealed record ReleaseInfo(string Tag, string Url);

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

/// <summary>Checks at startup, then at most once a day.</summary>
public sealed class UpdateCheckService(IServiceScopeFactory scopes, UpdateChecker checker, ILogger<UpdateCheckService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
                var value = await db.Settings.Where(s => s.Key == UpdateChecker.SettingKey).Select(s => s.Value).FirstOrDefaultAsync(stoppingToken);
                await checker.CheckAsync(UpdateChecker.ParseEnabled(value), UpdateChecker.Current, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogDebug(ex, "Update check failed"); }
            try { await Task.Delay(TimeSpan.FromHours(24), stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}
