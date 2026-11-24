using System.Diagnostics;
using System.Drawing.Printing;
using Mailflow.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Mailflow.Infrastructure.Printing;

public sealed class SumatraOptions
{
    /// <summary>Path to SumatraPDF.exe. Relative paths resolve against the app base directory.</summary>
    public string ExePath { get; set; } = Path.Combine("tools", "SumatraPDF", "SumatraPDF.exe");
    public int TimeoutSeconds { get; set; } = 60;
}

public interface IPrinterCatalog
{
    IReadOnlyList<string> GetInstalledPrinters();
}

public sealed class WindowsPrinterCatalog : IPrinterCatalog
{
    public IReadOnlyList<string> GetInstalledPrinters()
    {
        if (!OperatingSystem.IsWindows()) return [];
        return PrinterSettings.InstalledPrinters.Cast<string>().ToList();
    }
}

public sealed class PrintingException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class SumatraPrinterService(IPrinterCatalog catalog, IOptions<SumatraOptions> options) : IPrinterService
{
    public IReadOnlyList<string> GetInstalledPrinters() => catalog.GetInstalledPrinters();

    public static IReadOnlyList<string> BuildArguments(string pdfPath, string printerName, int copies, bool duplex) =>
    [
        "-print-to", printerName,
        "-silent",
        "-print-settings", $"{copies}x,{(duplex ? "duplexlong" : "simplex")}",
        pdfPath,
    ];

    public async Task PrintAsync(string pdfPath, string printerName, int copies, bool duplex, CancellationToken ct)
    {
        var o = options.Value;
        if (copies < 1) throw new PrintingException($"Nombre de copies invalide: {copies}.");
        if (!File.Exists(pdfPath)) throw new PrintingException($"PDF introuvable: {pdfPath}");
        if (!GetInstalledPrinters().Contains(printerName, StringComparer.OrdinalIgnoreCase))
            throw new PrintingException($"Imprimante non installee: {printerName}");
        var exe = Path.GetFullPath(o.ExePath, AppContext.BaseDirectory);
        if (!File.Exists(exe)) throw new PrintingException($"SumatraPDF introuvable: {exe}");

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var a in BuildArguments(pdfPath, printerName, copies, duplex)) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi) ?? throw new PrintingException("Impossible de demarrer SumatraPDF.");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(o.TimeoutSeconds));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { process.Kill(true); } catch { }
            throw new PrintingException($"Delai depasse ({o.TimeoutSeconds}s) pour l'impression sur {printerName}.");
        }
        if (process.ExitCode != 0)
        {
            var err = await process.StandardError.ReadToEndAsync(ct);
            throw new PrintingException($"SumatraPDF a echoue (code {process.ExitCode}): {err}".Trim());
        }
    }
}
