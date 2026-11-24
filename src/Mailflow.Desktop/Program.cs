using Mailflow.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace Mailflow.Desktop;

internal static class Program
{
    private const string MutexName = "Mailflow.Desktop.SingleInstance";
    private const string ShowEventName = "Mailflow.Desktop.Show";

    [STAThread]
    private static int Main(string[] args)
    {
        var options = LaunchOptions.Parse(args);

        // Service / headless: web host only, no UI.
        if (WindowsServiceHelpers.IsWindowsService() || options.Headless)
        {
            var app = MailflowHost.StartAsync(options.HostArgs, CancellationToken.None).GetAwaiter().GetResult();
            app.WaitForShutdownAsync().GetAwaiter().GetResult();
            return 0;
        }

        using var mutex = new Mutex(true, MutexName, out var first);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!first)
        {
            showEvent.Set();
            return 0;
        }

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(options.HostArgs)
            .Build();
        var url = new Uri(config["Kestrel:Endpoints:Http:Url"] ?? "http://127.0.0.1:5088");

        ApplicationConfiguration.Initialize();
        System.Windows.Forms.Application.Run(new TrayApp(url, options, showEvent));
        return 0;
    }
}
