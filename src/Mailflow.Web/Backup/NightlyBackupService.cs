using Mailflow.Application.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Web.Backup;

public sealed record BackupOptions(string ConnectionString, string FallbackRoot);

public sealed record BackupResult(bool Ok, string? FileName, string? Error);

/// <summary>SQLite VACUUM INTO a timestamped file under {DocumentsRoot}/backups, keeping the newest 14.</summary>
public sealed class BackupService(IServiceScopeFactory scopes, BackupOptions options, ILogger<BackupService> logger)
{
    public const int Keep = 14;
    public const string Prefix = "mailflow-";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>The file name is generated here (never user input); the result must stay inside the root.</summary>
    public static string BackupPath(string root, string fileName)
    {
        var fullRoot = Path.GetFullPath(root);
        var prefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, "backups", fileName));
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(full) != fileName)
            throw new InvalidOperationException("Le chemin de sauvegarde sort du dossier de documents.");
        return full;
    }

    public static void ApplyRetention(string backupDir)
    {
        var old = Directory.EnumerateFiles(backupDir, Prefix + "*.db")
            .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal).Skip(Keep);
        foreach (var f in old) File.Delete(f);
    }

    public async Task<BackupResult> RunAsync(CancellationToken ct, DateTime? now = null)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var root = await GetRootAsync(ct);
            var fileName = $"{Prefix}{now ?? DateTime.Now:yyyyMMdd-HHmm}.db";
            var target = BackupPath(root, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            await using (var conn = new SqliteConnection(options.ConnectionString))
            {
                await conn.OpenAsync(ct);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "VACUUM INTO $path";
                cmd.Parameters.AddWithValue("$path", target);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            ApplyRetention(Path.GetDirectoryName(target)!);
            logger.LogInformation("Sauvegarde créée : {File}", fileName);
            return new BackupResult(true, fileName, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Échec de la sauvegarde");
            return new BackupResult(false, null, ex.Message);
        }
        finally { Gate.Release(); }
    }

    private async Task<string> GetRootAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var v = await db.Settings.AsNoTracking().Where(s => s.Key == "DocumentsRoot").Select(s => s.Value).FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(v) ? options.FallbackRoot : v;
    }
}

public sealed class NightlyBackupService(BackupService backup, ILogger<NightlyBackupService> logger) : BackgroundService
{
    public static TimeSpan UntilNext(DateTime now)
    {
        var next = now.Date.AddHours(2);
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(UntilNext(DateTime.Now), stoppingToken);
                await backup.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.LogError(ex, "Sauvegarde nocturne : erreur inattendue"); }
        }
    }
}
