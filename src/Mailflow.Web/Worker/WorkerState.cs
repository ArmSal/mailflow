namespace Mailflow.Web.Worker;

/// <summary>Thread-safe status shared between the worker loop and the dashboard.</summary>
public sealed class WorkerState
{
    private volatile bool _paused;
    private volatile bool _running;
    private long _lastRunTicks;
    private long _nextRunTicks;
    private volatile string? _lastError;

    public bool IsPaused => _paused;
    public bool IsRunning => _running;
    public string? LastError => _lastError;
    public DateTime? LastRunUtc => Read(ref _lastRunTicks);
    public DateTime? NextRunUtc => Read(ref _nextRunTicks);

    public void Pause() => _paused = true;
    public void Resume() => _paused = false;

    internal void SetRunning(bool value) => _running = value;

    internal void SetLastRun(DateTime utc, string? error)
    {
        Interlocked.Exchange(ref _lastRunTicks, utc.Ticks);
        _lastError = error;
    }

    internal void SetNextRun(DateTime utc) => Interlocked.Exchange(ref _nextRunTicks, utc.Ticks);

    private static DateTime? Read(ref long ticks)
    {
        var t = Interlocked.Read(ref ticks);
        return t == 0 ? null : new DateTime(t, DateTimeKind.Utc);
    }
}
