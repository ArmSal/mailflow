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

// As a Windows service the working directory is System32: anchor everything to the exe folder.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
var dataDir = Path.Combine(AppContext.BaseDirectory, "data");

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
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db, documentsRoot);
}

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.UseRouting();
app.UseAuthentication();

// First run: no admin password yet -> force the setup page.
var configured = false;
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path;
    if (!configured && !path.StartsWithSegments("/Setup") && !path.StartsWithSegments("/lib")
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
app.MapStaticAssets().AllowAnonymous();
app.MapRazorPages().WithStaticAssets();

app.Run();

public partial class Program;
