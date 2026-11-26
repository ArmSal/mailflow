using System.Security.Cryptography;
using System.Text;
using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Application.Agents;

public sealed record AgentJobInfo(int Id, string PrinterName, int Copies, bool Duplex, string FileName);

/// <summary>The server side of the print agent protocol: tokens, the job queue and job results.</summary>
public sealed class AgentJobService(IAppDbContext db, IClock clock)
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

    public async Task<PrintAgent?> AuthenticateAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        token = token.Trim();
        // The password carries no agent id, so it is checked against each (few) agent's salted hash.
        var agent = (await db.PrintAgents.ToListAsync(ct)).FirstOrDefault(a => VerifyToken(token, a.TokenHash));
        if (agent is null) return null;
        agent.LastSeenAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return agent;
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
        return true;
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Length > 500 ? s[..500] : s;
}
