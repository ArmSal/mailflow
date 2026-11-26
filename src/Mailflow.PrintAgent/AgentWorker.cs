using System.Net;
using System.Net.Http.Json;

namespace Mailflow.Agent;

public interface IPdfPrinter
{
    Task PrintAsync(string pdfPath, string printerName, int copies, bool duplex, CancellationToken ct);
}

public interface IPrinterLister
{
    IReadOnlyList<string> List();
}

public sealed record AgentJob(int Id, string PrinterName, int Copies, bool Duplex, string FileName);

public sealed class RejectedPasswordException() : Exception("Mailflow a refusé le mot de passe de l'agent.");

/// <summary>One poll of the Mailflow server: take a job, download its PDF, print it, report the result.</summary>
public sealed class AgentWorker(HttpClient http, IPdfPrinter printer, Action<string> log)
{
    /// <summary>Returns true when a job was handled. False means nothing came in the waiting time.</summary>
    public async Task<bool> RunOnceAsync(CancellationToken ct)
    {
        // Mailflow holds this request open until a job is queued (up to 25 s), so a print arrives almost at once.
        using var next = await http.GetAsync("api/agent/jobs/next?wait=25", ct);
        if (next.StatusCode == HttpStatusCode.NoContent) return false;
        if (next.StatusCode == HttpStatusCode.Unauthorized) throw new RejectedPasswordException();
        next.EnsureSuccessStatusCode();
        var job = await next.Content.ReadFromJsonAsync<AgentJob>(ct) ?? throw new InvalidOperationException("Réponse vide du serveur.");

        var temp = Path.Combine(Path.GetTempPath(), $"mailflow-agent-{Guid.NewGuid():N}.pdf");
        try
        {
            using var pdf = await http.GetAsync($"api/agent/jobs/{job.Id}/pdf", ct);
            if (!pdf.IsSuccessStatusCode)
            {
                log($"Travail {job.Id} : PDF indisponible ({(int)pdf.StatusCode}).");
                return true;
            }
            await File.WriteAllBytesAsync(temp, await pdf.Content.ReadAsByteArrayAsync(ct), ct);

            string? error = null;
            try
            {
                await printer.PrintAsync(temp, job.PrinterName, job.Copies, job.Duplex, ct);
                log($"Travail {job.Id} : {job.FileName} imprimé sur {job.PrinterName}.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                error = ex.Message;
                log($"Travail {job.Id} : échec : {error}");
            }
            await ReportAsync(job.Id, error, ct);
        }
        finally
        {
            try { File.Delete(temp); } catch (IOException) { /* temp file, harmless */ }
        }
        return true;
    }

    /// <summary>Tells Mailflow which printers are installed on this PC, so they can be picked when a remote printer is set up.</summary>
    public async Task ReportPrintersAsync(IReadOnlyList<string> names, CancellationToken ct)
    {
        using var r = await http.PostAsJsonAsync("api/agent/printers", new { names }, ct);
        if (r.StatusCode == HttpStatusCode.Unauthorized) throw new RejectedPasswordException();
        r.EnsureSuccessStatusCode();
    }

    /// <summary>Retried: if the report is lost, Mailflow offers the job again after a few minutes and it would print twice.</summary>
    private async Task ReportAsync(int jobId, string? error, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var r = await http.PostAsJsonAsync($"api/agent/jobs/{jobId}/result", new { ok = error is null, error }, ct);
                r.EnsureSuccessStatusCode();
                return;
            }
            catch (HttpRequestException) when (attempt < 4)
            {
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
            }
        }
    }
}
