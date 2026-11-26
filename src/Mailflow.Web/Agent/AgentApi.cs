using Mailflow.Application.Agents;
using Mailflow.Domain.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Mailflow.Web.Agent;

public sealed record JobResult(bool Ok, string? Error);

public sealed record PrinterReport(List<string>? Names);

/// <summary>The only part of Mailflow a print agent talks to. Every call needs the agent's bearer token.</summary>
public static class AgentApi
{
    public const string Prefix = "/api/agent";
    public const string NameHeader = "X-Agent-Name";

    public static IEndpointRouteBuilder MapAgentApi(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup(Prefix).AllowAnonymous();

        // Lets the agent's setup check the URL and password without taking a job.
        g.MapGet("/ping", async (HttpContext ctx, AgentJobService svc, AgentAuthThrottle throttle) =>
        {
            var (agent, reject) = await Authenticate(ctx, svc, throttle);
            return agent is null ? reject! : Results.Json(new { agent.Name });
        });

        // The printers installed on the agent's PC, so Mailflow can offer them when a remote printer is set up.
        g.MapPost("/printers", async (PrinterReport body, HttpContext ctx, AgentJobService svc, AgentAuthThrottle throttle) =>
        {
            var (agent, reject) = await Authenticate(ctx, svc, throttle);
            if (agent is null) return reject!;
            await svc.SetPrintersAsync(agent, body.Names ?? [], ctx.RequestAborted);
            return Results.Ok();
        });

        g.MapGet("/jobs/next", async (HttpContext ctx, AgentJobService svc, AgentAuthThrottle throttle) =>
        {
            var (agent, reject) = await Authenticate(ctx, svc, throttle);
            if (agent is null) return reject!;
            return await svc.ClaimNextAsync(agent, ctx.RequestAborted) is { } job ? Results.Json(job) : Results.NoContent();
        });

        g.MapGet("/jobs/{id:int}/pdf", async (int id, HttpContext ctx, AgentJobService svc, AgentAuthThrottle throttle) =>
        {
            var (agent, reject) = await Authenticate(ctx, svc, throttle);
            if (agent is null) return reject!;
            if (await svc.GetClaimedAsync(agent, id, ctx.RequestAborted) is not { } job) return Results.NotFound();
            if (!File.Exists(job.PdfPath))
            {
                await svc.CompleteAsync(agent, id, false, "PDF introuvable sur le serveur Mailflow.", ctx.RequestAborted);
                return Results.NotFound();
            }
            return Results.File(job.PdfPath, "application/pdf");
        });

        g.MapPost("/jobs/{id:int}/result", async (int id, JobResult body, HttpContext ctx, AgentJobService svc, AgentAuthThrottle throttle) =>
        {
            var (agent, reject) = await Authenticate(ctx, svc, throttle);
            if (agent is null) return reject!;
            return await svc.CompleteAsync(agent, id, body.Ok, body.Error, ctx.RequestAborted) ? Results.Ok() : Results.NotFound();
        });

        return app;
    }

    /// <summary>The agent, or the response to send instead (401 wrong password, 429 too many wrong passwords from this address).</summary>
    private static async Task<(PrintAgent? Agent, IResult? Reject)> Authenticate(HttpContext ctx, AgentJobService svc, AgentAuthThrottle throttle)
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var now = DateTime.UtcNow;
        if (throttle.IsBlocked(ip, now)) return (null, Results.StatusCode(StatusCodes.Status429TooManyRequests));

        var header = ctx.Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..] : null;
        var agent = await svc.AuthenticateAsync(Uri.UnescapeDataString(ctx.Request.Headers[NameHeader].ToString()), token, ctx.RequestAborted);
        if (agent is null)
        {
            throttle.Fail(ip, now);
            return (null, Results.Unauthorized());
        }
        throttle.Succeed(ip);
        return (agent, null);
    }
}
