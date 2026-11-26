using Mailflow.Application.Agents;
using Mailflow.Domain.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Mailflow.Web.Agent;

public sealed record JobResult(bool Ok, string? Error);

/// <summary>The only part of Mailflow a print agent talks to. Every call needs the agent's bearer token.</summary>
public static class AgentApi
{
    public const string Prefix = "/api/agent";

    public static IEndpointRouteBuilder MapAgentApi(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup(Prefix).AllowAnonymous();

        // Lets the agent's setup check the URL and password without taking a job.
        g.MapGet("/ping", async (HttpContext ctx, AgentJobService svc) =>
            await Authenticate(ctx, svc) is { } agent ? Results.Json(new { agent.Name }) : Results.Unauthorized());

        g.MapGet("/jobs/next", async (HttpContext ctx, AgentJobService svc) =>
        {
            if (await Authenticate(ctx, svc) is not { } agent) return Results.Unauthorized();
            return await svc.ClaimNextAsync(agent, ctx.RequestAborted) is { } job ? Results.Json(job) : Results.NoContent();
        });

        g.MapGet("/jobs/{id:int}/pdf", async (int id, HttpContext ctx, AgentJobService svc) =>
        {
            if (await Authenticate(ctx, svc) is not { } agent) return Results.Unauthorized();
            if (await svc.GetClaimedAsync(agent, id, ctx.RequestAborted) is not { } job) return Results.NotFound();
            if (!File.Exists(job.PdfPath))
            {
                await svc.CompleteAsync(agent, id, false, "PDF introuvable sur le serveur Mailflow.", ctx.RequestAborted);
                return Results.NotFound();
            }
            return Results.File(job.PdfPath, "application/pdf");
        });

        g.MapPost("/jobs/{id:int}/result", async (int id, JobResult body, HttpContext ctx, AgentJobService svc) =>
        {
            if (await Authenticate(ctx, svc) is not { } agent) return Results.Unauthorized();
            return await svc.CompleteAsync(agent, id, body.Ok, body.Error, ctx.RequestAborted) ? Results.Ok() : Results.NotFound();
        });

        return app;
    }

    private static Task<PrintAgent?> Authenticate(HttpContext ctx, AgentJobService svc)
    {
        var header = ctx.Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..] : null;
        return svc.AuthenticateAsync(token, ctx.RequestAborted);
    }
}
