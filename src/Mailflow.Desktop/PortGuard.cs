namespace Mailflow.Desktop;

public enum PortVerdict { NoListener, Genuine, Impostor }

/// <summary>
/// Startup only proves who owns the port at that moment. While the window stays open the genuine server could die and
/// another local program bind the port, so the window re-checks before EACH request it makes to the dashboard (no caching:
/// a check costs about 0.1 ms). What remains is the instant between the check and the connection, which an ownership check
/// cannot close; a single-user PC with a local attacker who must time the real server's exit is the accepted residual risk.
/// </summary>
public static class PortGuard
{
    /// <summary>Nobody listening is fine (the window shows its "not reachable" page); a listener must be this executable.</summary>
    public static PortVerdict Decide(uint listenerPid, Func<uint, string?> imagePath, string? currentExePath)
        => listenerPid == 0 ? PortVerdict.NoListener
         : AttachOrStart.SamePath(imagePath(listenerPid), currentExePath) ? PortVerdict.Genuine : PortVerdict.Impostor;

    /// <summary>Runs a check; if it cannot tell, the answer is Impostor (fail closed).</summary>
    public static PortVerdict Check(Func<PortVerdict> check)
    {
        try { return check(); }
        catch (Exception) { return PortVerdict.Impostor; }
    }
}
