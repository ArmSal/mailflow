using Mailflow.Application.Abstractions;
using Mailflow.Application.Processing;
using Mailflow.Domain.Rules;
using Mailflow.Infrastructure;
using Mailflow.Infrastructure.Persistence;
using Mailflow.Web.Auth;
using Mailflow.Web.Backup;
using Mailflow.Web.Pages.Printers;
using Mailflow.Web.Pages.Rules;
using Mailflow.Web.Services;
using Mailflow.Web.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Serilog;
using System.Net;
using System.Threading.RateLimiting;

namespace Mailflow.Web;

/// <summary>Builds, migrates, seeds and starts the web host; shared by the console entry point and the desktop shell.</summary>
public static class MailflowHost
{
    public static async Task<WebApplication> StartAsync(string[] args, CancellationToken ct)
    {
        // As a Windows service the working directory is System32: anchor everything to the exe folder.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        ApplicationName = typeof(MailflowHost).Assembly.GetName().Name,
        });
        var dataDir = Path.Combine(AppContext.BaseDirectory, "data");

        // Build output (dotnet run, Debug) has no copy of wwwroot: static files come from the build's runtime manifest,
        // which ASP.NET only loads by itself in Development (otherwise every asset is served empty). A published
        // release ships wwwroot next to the exe and has no such manifest, so this does nothing there.
        var staticAssetsManifest = Path.Combine(AppContext.BaseDirectory,
            System.Reflection.Assembly.GetEntryAssembly()!.GetName().Name + ".staticwebassets.runtime.json");
        if (File.Exists(staticAssetsManifest))
        {
            builder.Configuration["StaticWebAssets"] = staticAssetsManifest;
            builder.WebHost.UseStaticWebAssets();
        }

        builder.Host.UseWindowsService(o => o.ServiceName = "Mailflow");
        builder.Host.UseSerilog((ctx, cfg) => cfg
            .ReadFrom.Configuration(ctx.Configuration)
            .WriteTo.Console()
            .WriteTo.File(Path.Combine(dataDir, "logs", "mailflow-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30));

        try { System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal; }
        catch (Exception) { /* not fatal */ }

        var services = builder.Services;
        services.AddRazorPages();
        services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.LoginPath = "/Login";
                o.AccessDeniedPath = "/Login";
                o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
                o.SlidingExpiration = true;
                o.Cookie.Name = "mailflow.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Strict;
            });
        // Slow down password guessing: at most 5 login attempts per minute per client address.
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                ctx.Request.Method == HttpMethods.Post && ctx.Request.Path.StartsWithSegments("/Login")
                    ? RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })
                    : RateLimitPartition.GetNoLimiter("other"));
        });
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        services.AddSingleton<IPasswordHasher<object>, PasswordHasher<object>>();
        services.AddSingleton<InMemoryAdminPasswordStore>();
        services.AddScoped<IAdminPasswordStore>(sp =>
            sp.GetService<IAppDbContext>() is { } db
                ? new DbAdminPasswordStore(db)
                : sp.GetRequiredService<InMemoryAdminPasswordStore>());

        Directory.CreateDirectory(dataDir);
        var documentsRoot = builder.Configuration["Storage:DocumentsRoot"] ?? Path.Combine(dataDir, "documents");
        var connectionString = $"Data Source={Path.Combine(dataDir, "mailflow.db")}";
        services.AddPersistence(connectionString);
        services.AddPdfAndStorage(o => o.DocumentsRoot = documentsRoot);
        services.AddMailPrintingSecurity();
        services.AddSingleton(new RuleEngine());
        services.AddScoped<MessagePipeline>();
        services.AddScoped<MailboxProcessor>();
        services.AddScoped<IRuleTester, MailboxRuleTester>();
        services.AddScoped<Mailflow.Application.Queries.DocumentSearch>();
        services.AddScoped<Mailflow.Application.Commands.Reprint>();
        services.AddScoped<Mailflow.Application.Commands.Reclassify>();
        services.AddSingleton<ITestPageProvider>(new GeneratedTestPageProvider(Path.Combine(dataDir, "testpage.pdf")));

        services.AddSingleton<IClock, SystemClock>();
        services.Configure<WorkerOptions>(builder.Configuration.GetSection(WorkerOptions.Section));
        services.AddSingleton<RunNowSignal>();
        services.AddSingleton<WorkerState>();
        services.AddSingleton<IMailboxRunner, ScopedMailboxRunner>();
        services.AddHostedService<MailflowWorker>();
        services.AddSingleton(new BackupOptions(connectionString, documentsRoot));
        services.AddSingleton<BackupService>();
        services.AddHostedService<NightlyBackupService>();

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MailflowDbContext>();
            await db.Database.MigrateAsync(ct);
            await DbSeeder.SeedAsync(db, documentsRoot);
        }

        if (!app.Environment.IsDevelopment())
            app.UseExceptionHandler("/Error");

        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();

        // First run: no admin password yet -> force the setup page.
        var configured = false;
        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path;
            // First-run setup is open to anyone until a password exists, so only the machine itself may reach it.
            if (path.StartsWithSegments("/Setup") && !IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress ?? IPAddress.None))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            if (!configured && !path.StartsWithSegments("/Setup") && path != "/healthz" && !path.StartsWithSegments("/lib")
                && !path.StartsWithSegments("/css") && !path.StartsWithSegments("/js") && path != "/favicon.ico")
            {
                var store = ctx.RequestServices.GetRequiredService<IAdminPasswordStore>();
                if (string.IsNullOrEmpty(await store.GetHashAsync(ctx.RequestAborted)))
                {
                    ctx.Response.Redirect("/Setup");
                    return;
                }
                configured = true;
            }
            await next();
        });

        app.UseAuthorization();
        // The manifest is named after the entry assembly (Mailflow or Mailflow.Web), not the hosted web assembly.
        app.MapStaticAssets(System.Reflection.Assembly.GetEntryAssembly()!.GetName().Name + ".staticwebassets.endpoints.json").AllowAnonymous();
        app.MapRazorPages().WithStaticAssets();
        // Lets the desktop shell detect an already running instance; reveals nothing.
        app.MapGet("/healthz", () => "Mailflow").AllowAnonymous();

        await app.StartAsync(ct);

        return app;
    }
}
