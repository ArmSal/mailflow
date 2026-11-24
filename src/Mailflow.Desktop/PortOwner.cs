using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Mailflow.Desktop;

/// <summary>Finds which program owns the TCP listener on a local port. Every failure returns null, nothing throws.</summary>
internal static class PortOwner
{
    private const int AfInet = 2, AfInet6 = 23, TcpTableOwnerPidListener = 3;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int family, int tableClass, uint reserved);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder name, ref int size);

    /// <summary>Image path of the process listening on 127.0.0.1:port (or 0.0.0.0, then ::1 / ::), or null if unknown.</summary>
    public static string? GetListenerImagePath(int port)
    {
        try
        {
            var pid = FindListenerPid(port);
            return pid == 0 ? null : GetImagePath(pid);
        }
        catch (Exception) { return null; }
    }

    /// <summary>A listener bound to the exact loopback address wins over a wildcard one (that is what Windows routes to).</summary>
    public static uint FindListenerPid(int port)
    {
        var pid = Scan(AfInet, port, 24, 4, 4, 8, 20, specific: a => a.SequenceEqual(new byte[] { 127, 0, 0, 1 }), wildcard: a => a.IndexOfAnyExcept((byte)0) < 0);
        return pid != 0 ? pid
             : Scan(AfInet6, port, 56, 16, 0, 20, 52, specific: a => a.SequenceEqual(System.Net.IPAddress.IPv6Loopback.GetAddressBytes()), wildcard: a => a.IndexOfAnyExcept((byte)0) < 0);
    }

    // Row layouts: v4 = state(4) addr(4) port(4) remoteAddr remotePort pid; v6 = addr(16) scope(4) port(4) ... pid. The pid offset is the row's last field.
    private static uint Scan(int family, int port, int rowSize, int addrLen, int addrOffset, int portOffset, int pidOffset,
        Func<byte[], bool> specific, Func<byte[], bool> wildcard)
    {
        var size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, TcpTableOwnerPidListener, 0);
        for (var attempt = 0; attempt < 5 && size > 0; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedTcpTable(buffer, ref size, false, family, TcpTableOwnerPidListener, 0) != 0) continue; // size grew, retry // size grew, retry
                var count = Marshal.ReadInt32(buffer);
                uint wild = 0;
                for (var i = 0; i < count; i++)
                {
                    var row = buffer + 4 + i * rowSize;
                    var raw = Marshal.ReadInt32(row, portOffset);
                    var rowPort = ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF);
                    if (rowPort != port) continue;
                    var addr = new byte[addrLen];
                    Marshal.Copy(row + addrOffset, addr, 0, addrLen);
                    var pid = (uint)Marshal.ReadInt32(row, pidOffset);
                    if (specific(addr)) return pid;
                    if (wildcard(addr)) wild = pid;
                }
                return wild;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return 0;
    }

    public static string? GetImagePath(uint pid)
    {
        using var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle.IsInvalid) return null;
        var name = new StringBuilder(1024);
        var size = name.Capacity;
        return QueryFullProcessImageNameW(handle, 0, name, ref size) ? name.ToString() : null;
    }
}
