using Mailflow.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Application.Processing;

/// <summary>Tells the person at the PC about a failure that survived every retry (a Windows toast in the desktop host).</summary>
public interface IFailureNotifier
{
    void Notify(string title, string message);
}

/// <summary>Used when there is no desktop host (service, console, tests).</summary>
public sealed class NullFailureNotifier : IFailureNotifier
{
    public void Notify(string title, string message) { }
}

/// <summary>Retries an operation after each delay in <see cref="Delays"/> (so it runs at most Delays.Count + 1 times).</summary>
public sealed record RetryPolicy(IReadOnlyList<TimeSpan> Delays, Func<TimeSpan, CancellationToken, Task>? Delay = null)
{
    public static RetryPolicy Default { get; } = new([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10)]);

    /// <summary>Same number of attempts as <see cref="Default"/> without waiting (for tests).</summary>
    public static RetryPolicy Instant { get; } = new([TimeSpan.Zero, TimeSpan.Zero], (_, _) => Task.CompletedTask);

    public async Task RunAsync(Func<Task> operation, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { await operation(); return; }
            catch (Exception) when (attempt < Delays.Count && !ct.IsCancellationRequested)
            {
                await (Delay ?? Task.Delay)(Delays[attempt], ct);
            }
        }
    }
}

public static class FailureAlerts
{
    /// <summary>Setting that turns toasts off when its value is "false"; on by default.</summary>
    public const string SettingKey = "ToastNotifications";

    public static bool ParseEnabled(string? value) => !string.Equals(value?.Trim(), "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>Never throws: a toast must not turn into a second failure.</summary>
    public static async Task NotifyAsync(IAppDbContext db, IFailureNotifier notifier, string title, string message, CancellationToken ct)
    {
        try
        {
            var value = (await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == SettingKey, ct))?.Value;
            if (ParseEnabled(value)) notifier.Notify(title, message);
        }
        catch (Exception) { /* notification is best effort */ }
    }
}
