using System.Drawing.Printing;

namespace Mailflow.Agent;

/// <summary>The printers installed on this PC (local ones and network printers added to Windows).</summary>
public sealed class WindowsPrinterLister : IPrinterLister
{
    public IReadOnlyList<string> List() =>
        OperatingSystem.IsWindows() ? PrinterSettings.InstalledPrinters.Cast<string>().ToList() : [];
}
