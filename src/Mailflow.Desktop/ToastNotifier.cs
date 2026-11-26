using Mailflow.Application.Processing;

namespace Mailflow.Desktop;

/// <summary>Failure notifier of the desktop host; <paramref name="show"/> raises the Windows notification.</summary>
internal sealed class ToastNotifier(Action<string, string> show) : IFailureNotifier
{
    public void Notify(string title, string message)
    {
        try { show(title, message.Length > 250 ? message[..250] : message); }
        catch (Exception) { /* a notification must never crash the worker */ }
    }
}
