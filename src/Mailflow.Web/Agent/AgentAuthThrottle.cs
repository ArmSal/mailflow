using System.Collections.Concurrent;

namespace Mailflow.Web.Agent;

/// <summary>
/// Refuses an address after repeated wrong agent passwords, before any (deliberately slow) hash is computed.
/// Bounds both guessing and the CPU an unauthenticated caller can burn.
/// </summary>
public sealed class AgentAuthThrottle
{
    public const int MaxFailures = 10;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, (int Count, DateTime Since)> _failures = new();

    public bool IsBlocked(string ip, DateTime now) =>
        _failures.TryGetValue(ip, out var f) && now - f.Since < Window && f.Count >= MaxFailures;

    public void Fail(string ip, DateTime now)
    {
        if (_failures.Count > 1000)
            foreach (var old in _failures.Where(kv => now - kv.Value.Since >= Window).Select(kv => kv.Key).ToList()) _failures.TryRemove(old, out _);
        _failures.AddOrUpdate(ip, (1, now), (_, f) => now - f.Since >= Window ? (1, now) : (f.Count + 1, f.Since));
    }

    public void Succeed(string ip) => _failures.TryRemove(ip, out _);
}
