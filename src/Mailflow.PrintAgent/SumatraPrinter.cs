using System.Diagnostics;

namespace Mailflow.Agent;

/// <summary>Prints a PDF silently with the portable SumatraPDF (a separate program, see THIRD-PARTY-NOTICES.md).</summary>
public sealed class SumatraPrinter(string exePath, int timeoutSeconds = 60) : IPdfPrinter
{
    public static IReadOnlyList<string> BuildArguments(string pdfPath, string printerName, int copies, bool duplex) =>
    [
        "-print-to", printerName,
        "-silent",
        "-print-settings", $"{copies}x,{(duplex ? "duplexlong" : "simplex")}",
        pdfPath,
    ];

    public async Task PrintAsync(string pdfPath, string printerName, int copies, bool duplex, CancellationToken ct)
    {
        if (copies is < 1 or > 99) throw new InvalidOperationException($"Nombre de copies invalide : {copies}.");
        var exe = Path.GetFullPath(exePath, AppContext.BaseDirectory);
        if (!File.Exists(exe)) throw new FileNotFoundException($"SumatraPDF introuvable : {exe}");

        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in BuildArguments(pdfPath, printerName, copies, duplex)) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Impossible de démarrer SumatraPDF.");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try { await process.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { process.Kill(true); } catch (InvalidOperationException) { /* already gone */ }
            throw new TimeoutException($"Délai dépassé ({timeoutSeconds} s) pour l'impression sur {printerName}.");
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"SumatraPDF a échoué (code {process.ExitCode}) sur l'imprimante « {printerName} » : {await process.StandardError.ReadToEndAsync(ct)}".Trim());
    }
}
