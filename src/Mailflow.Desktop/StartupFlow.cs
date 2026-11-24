using System.Net.Sockets;

namespace Mailflow.Desktop;

public enum StartupOutcome { Attached, Started, Refused, PortBusy, Failed }

/// <summary>Start-up sequence of the window app, free of UI so tests can drive it. Dialogs go through showError.</summary>
public static class StartupFlow
{
    public static int ExitCode(StartupOutcome o) => o switch
    {
        StartupOutcome.Refused => 2,
        StartupOutcome.PortBusy => 3,
        StartupOutcome.Failed => 1,
        _ => 0,
    };

    public static async Task<StartupOutcome> RunAsync(int port, Func<CancellationToken, Task<string?>> probe, Func<string?> ownerImagePath,
        string? currentExePath, Func<Task> startHost, Action<string> showError)
    {
        var mode = await AttachOrStart.DecideAsync(probe, ownerImagePath, currentExePath);
        if (mode == ServerMode.Refuse)
        {
            showError(string.Format(AttachOrStart.RefuseMessageFormat, port));
            return StartupOutcome.Refused;
        }
        if (mode == ServerMode.Attach) return StartupOutcome.Attached;
        try
        {
            await startHost();
            return StartupOutcome.Started;
        }
        catch (Exception ex) when (IsPortBusy(ex))
        {
            showError($"Le port {port} est déjà utilisé par un autre programme. Fermez ce programme ou changez le port dans appsettings.json (Kestrel), puis relancez Mailflow.");
            return StartupOutcome.PortBusy;
        }
        catch (Exception ex)
        {
            showError($"Impossible de démarrer Mailflow : {ex.Message}");
            return StartupOutcome.Failed;
        }
    }

    public static bool IsPortBusy(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
            if (ex is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse }
                || ex.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
