using System.Security.Cryptography;
using System.Text;
using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Application.Agents;

public sealed record AgentJobInfo(int Id, string PrinterName, int Copies, bool Duplex, string FileName);

/// <summary>The server side of the print agent protocol: tokens, the job queue and job results.</summary>
public sealed class AgentJobService(IAppDbContext db, IClock clock, Processing.IFailureNotifier? notifier = null)
{
    /// <summary>A job handed out but never reported (agent crashed or lost the network) is offered again after this.</summary>
    public static readonly TimeSpan ResendAfter = TimeSpan.FromMinutes(5);

    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private const int Iterations = 210_000;

    /// <summary>Salted, slow hash ("pbkdf2$iterations$salt$hash"): the password may be one the administrator chose.</summary>
    public static string HashToken(string token)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(token), salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool VerifyToken(string token, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2" || !int.TryParse(parts[1], out var iterations) || iterations < 1) return false;
        try
        {
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(token), Convert.FromBase64String(parts[2]), iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }

    /// <summary>Caller checks <c>printer.PrintAgentId</c> first.</summary>
    public static PrintJob NewJob(Printer printer, string pdfPath, int copies, DateTime now) => new()
    {
        PrintAgentId = printer.PrintAgentId!.Value, PdfPath = pdfPath, PrinterName = printer.WindowsName,
        Copies = copies, Duplex = printer.Duplex, Status = PrintJobStatus.Pending, CreatedAt = now,
    };

    /// <summary>Hash checked when the name is unknown, so an unknown name takes as long to refuse as a wrong password.</summary>
    private static readonly string UnknownAgentHash = HashToken("unknown-agent");

    /// <summary>The agent is identified by its name and proved by its password; only that agent's hash is computed.</summary>
    public async Task<PrintAgent?> AuthenticateAsync(string? name, string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(token)) return null;
        name = name.Trim().ToLowerInvariant();
        token = token.Trim();
        var agent = await db.PrintAgents.FirstOrDefaultAsync(a => a.Name.ToLower() == name, ct);
        if (agent is null) { VerifyToken(token, UnknownAgentHash); return null; }
        if (!VerifyToken(token, agent.TokenHash)) return null;
        agent.LastSeenAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return agent;
    }

    /// <summary>Stores the printers installed on the agent's PC (what the printer form offers for a remote printer).</summary>
    public async Task SetPrintersAsync(PrintAgent agent, IEnumerable<string> names, CancellationToken ct)
    {
        var clean = names.Select(n => n?.Trim() ?? "").Where(n => n.Length is > 0 and <= 260).Distinct(StringComparer.OrdinalIgnoreCase).Take(200);
        agent.PrinterNames = string.Join("\n", clean);
        agent.PrintersReportedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public static IReadOnlyList<string> PrinterList(PrintAgent agent) =>
        string.IsNullOrEmpty(agent.PrinterNames) ? [] : agent.PrinterNames.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>How often a request that is waiting for a job looks again.</summary>
    public static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(1);
    /// <summary>The longest a request may be held open; the agent then simply asks again.</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Hands over the next job, or keeps the request open (checking every second, for at most <paramref name="wait"/>)
    /// until one is queued: a print reaches a connected agent within about a second. <paramref name="delay"/> is for tests.
    /// </summary>
    public async Task<AgentJobInfo?> ClaimNextAsync(PrintAgent agent, TimeSpan wait, CancellationToken ct, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        delay ??= Task.Delay;
        if (wait > MaxWait) wait = MaxWait;
        var waited = TimeSpan.Zero;
        while (true)
        {
            if (await ClaimNextAsync(agent, ct) is { } job) return job;
            if (waited >= wait) return null;
            await delay(PollEvery, ct);
            waited += PollEvery;
        }
    }

    public async Task<AgentJobInfo?> ClaimNextAsync(PrintAgent agent, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var stale = now - ResendAfter;
        var job = await db.PrintJobs
            .Where(j => j.PrintAgentId == agent.Id && (j.Status == PrintJobStatus.Pending || (j.Status == PrintJobStatus.Sent && j.SentAt < stale)))
            .OrderBy(j => j.Id).FirstOrDefaultAsync(ct);
        if (job is null) return null;
        job.Status = PrintJobStatus.Sent;
        job.SentAt = now;
        await db.SaveChangesAsync(ct);
        return new AgentJobInfo(job.Id, job.PrinterName, job.Copies, job.Duplex, Path.GetFileName(job.PdfPath));
    }

    /// <summary>The job, only if it belongs to this agent and was handed out to it.</summary>
    public Task<PrintJob?> GetClaimedAsync(PrintAgent agent, int jobId, CancellationToken ct) =>
        db.PrintJobs.FirstOrDefaultAsync(j => j.Id == jobId && j.PrintAgentId == agent.Id && j.Status == PrintJobStatus.Sent, ct);

    public async Task<bool> CompleteAsync(PrintAgent agent, int jobId, bool ok, string? error, CancellationToken ct)
    {
        var job = await GetClaimedAsync(agent, jobId, ct);
        if (job is null) return false;
        var now = clock.UtcNow;
        job.Status = ok ? PrintJobStatus.Done : PrintJobStatus.Failed;
        job.CompletedAt = now;
        job.Error = ok ? null : Trim(error);
        if (job.DocumentId is int docId && await db.Documents.FirstOrDefaultAsync(d => d.Id == docId, ct) is { } doc)
        {
            doc.PrintedAt = ok ? now : doc.PrintedAt;
            doc.PrintError = ok ? null : job.Error ?? "Échec de l'impression sur l'agent.";
        }
        await db.SaveChangesAsync(ct);
        if (!ok)
            await Processing.FailureAlerts.NotifyAsync(db, notifier ?? new Processing.NullFailureNotifier(), "Échec de l'impression",
                $"{job.PrinterName} : {job.Error ?? "Échec de l'impression sur l'agent."}", ct);
        return true;
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Length > 500 ? s[..500] : s;
}
