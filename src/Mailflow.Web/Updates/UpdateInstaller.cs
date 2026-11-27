using System.Diagnostics;
using Mailflow.Application.Updates;
using Mailflow.Web.Backup;
using Microsoft.Win32;

namespace Mailflow.Web.Updates;

/// <summary>Backs up, downloads and verifies the new Setup.exe, then runs it silently over this installation.</summary>
public sealed class UpdateInstaller(HttpClient http, BackupService backup)
{
    public static string WorkDir => Path.Combine(AppContext.BaseDirectory, "data", "updates");

    /// <summary>One-click install only makes sense for a copy put there by Setup.exe (same folder as the Add/Remove entry).</summary>
    public static bool IsSetupInstall()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Mailflow");
            var loc = k?.GetValue("InstallLocation") as string;
            return !string.IsNullOrEmpty(loc) && string.Equals(Path.GetFullPath(loc).TrimEnd('\\'),
                Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
    }

    /// <summary>Returns the verified installer, or the reason it cannot be used.</summary>
    public async Task<PrepareResult> PrepareAsync(string tag, CancellationToken ct)
    {
        var r = await UpdateDownloader.PrepareAsync(http, UpdateProduct.Mailflow, tag, UpdateChecker.Current, WorkDir, ct);
        if (r.Error != PrepareError.None) return r;
        // Migrations cannot be undone, so keep a restore point of the database first.
        var b = await backup.RunAsync(ct);
        return b.Ok ? r : new PrepareResult(null, PrepareError.BackupFailed);
    }

    /// <summary>Setup stops this process, replaces the files (data\ is kept) and starts the new version minimized.</summary>
    public static void Launch(string installerPath)
        => Process.Start(new ProcessStartInfo(installerPath, "/S /minimized") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(installerPath)! })?.Dispose();
}
