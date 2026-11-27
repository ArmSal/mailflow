namespace Mailflow.Desktop;

public enum PortVerdict { NoListener, Genuine, Impostor }

/// <summary>
/// Startup only proves who owns the port at that moment. While the window stays open the genuine server could die and
/// another local program bind the port, so the window re-checks before each request it makes to the dashboard.
/// </summary>
public static class PortGuard
{
    /// <summary>Nobody listening is fine (the window shows its "not reachable" page); a listener must be this executable.</summary>
    public static PortVerdict Decide(uint listenerPid, Func<uint, string?> imagePath, string? currentExePath)
        => listenerPid == 0 ? PortVerdict.NoListener
         : AttachOrStart.SamePath(imagePath(listenerPid), currentExePath) ? PortVerdict.Genuine : PortVerdict.Impostor;

    /// <summary>Keeps the last verdict for a short time so a page with many resources does not scan the TCP table each time.</summary>
    public sealed class Cache(Func<PortVerdict> check, Func<DateTime> utcNow, TimeSpan ttl)
    {
        private PortVerdict _verdict;
        private DateTime _checkedAt = DateTime.MinValue;

        public PortVerdict Current()
        {
            var now = utcNow();
            if (_checkedAt != DateTime.MinValue && now - _checkedAt < ttl) return _verdict;
            try { _verdict = check(); }
            catch (Exception) { _verdict = PortVerdict.Impostor; }   // cannot tell: fail closed
            _checkedAt = now;
            return _verdict;
        }
    }
}
