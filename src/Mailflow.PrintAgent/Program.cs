using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Mailflow.Agent;

internal static class Program
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "MailflowPrintAgent";

    [DllImport("kernel32.dll")] private static extern bool FreeConsole();

    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "agent.log");

    private static async Task<int> Main(string[] args)
    {
        var mode = args.FirstOrDefault()?.ToLowerInvariant();
        switch (mode)
        {
            case "--setup": return await SetupAsync();
            case "--uninstall": return Uninstall();
            case "--run": if (OperatingSystem.IsWindows()) FreeConsole(); return await RunAsync();
            case null:
                // Double-clicked before any setup: guide the user instead of failing.
                return File.Exists(AgentConfig.DefaultPath) ? await RunAsync() : await SetupAsync();
            default:
                Console.WriteLine("Utilisation : MailflowPrintAgent.exe [--setup | --run | --uninstall]");
                return 2;
        }
    }

    private static async Task<int> RunAsync()
    {
        AgentConfig? config;
        try { config = AgentConfig.Load(AgentConfig.DefaultPath); }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException) { Log("agent.json illisible : " + ex.Message); return 1; }
        if (config is null) { Log("agent.json introuvable. Lancez MailflowPrintAgent.exe --setup."); return 1; }
        if (config.Validate() is { } problem) { Log("Configuration invalide : " + problem); return 1; }

        using var http = config.NewHttpClient();
        var worker = new AgentWorker(http, new SumatraPrinter(config.SumatraPath), Log);
        var printers = new WindowsPrinterLister();
        var nextPrinterReport = DateTime.MinValue;
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        Log($"Agent « {config.AgentName} » démarré, serveur {config.ServerUrl}.");

        var failures = 0;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow >= nextPrinterReport)
                {
                    await worker.ReportPrintersAsync(printers.List(), stop.Token);
                    nextPrinterReport = DateTime.UtcNow.AddMinutes(10);
                }
                var asked = DateTime.UtcNow;
                var handled = await worker.RunOnceAsync(stop.Token);
                failures = 0;
                if (handled) continue;
                // Mailflow normally holds the request open and the next one starts at once. If it answered straight away
                // (an older Mailflow without waiting), pause so the agent does not hammer it.
                if (DateTime.UtcNow - asked < TimeSpan.FromSeconds(2)) await Task.Delay(TimeSpan.FromSeconds(config.PollSeconds), stop.Token);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or RejectedPasswordException)
            {
                // Mailflow unreachable or password refused: wait longer each time (up to a minute) and keep trying.
                if (failures++ == 0 || ex is RejectedPasswordException) Log(ex is RejectedPasswordException ? ex.Message : "Mailflow injoignable : " + ex.Message);
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, 5 * failures)), stop.Token); } catch (OperationCanceledException) { break; }
            }
            catch (Exception ex)
            {
                Log("Erreur inattendue : " + ex.Message);
                try { await Task.Delay(TimeSpan.FromSeconds(30), stop.Token); } catch (OperationCanceledException) { break; }
            }
        }
        Log("Agent arrêté.");
        return 0;
    }

    private static async Task<int> SetupAsync()
    {
        Console.WriteLine("Mailflow Print Agent : configuration\n");
        var config = AgentConfig.Load(AgentConfig.DefaultPath) ?? new AgentConfig();

        Console.Write($"Adresse de Mailflow (par exemple http://100.64.0.5:5089){(config.ServerUrl.Length > 0 ? $" [{config.ServerUrl}]" : "")} : ");
        var url = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(url)) config.ServerUrl = url;
        Console.Write($"Nom de cet agent, tel que créé dans Mailflow{(config.AgentName.Length > 0 ? $" [{config.AgentName}]" : "")} : ");
        var agentName = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(agentName)) config.AgentName = agentName;
        Console.Write("Mot de passe de l'agent (le même que dans Mailflow) : ");
        var password = ReadHidden();
        if (string.IsNullOrWhiteSpace(password)) { Console.WriteLine("Mot de passe vide : abandon."); return 1; }
        config.SetPassword(password);
        if (config.Validate() is { } problem) { Console.WriteLine(problem); return 1; }

        if (config.ServerUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            Console.Write("Vérification du certificat de Mailflow... ");
            var probe = await CertificateProbe.RunAsync(config.BaseAddress);
            if (probe.Fingerprint is null) { Console.WriteLine("échec : " + probe.Error); return 1; }
            var known = string.IsNullOrWhiteSpace(config.ServerCertSha256) ? null : AgentConfig.NormalizeFingerprint(config.ServerCertSha256);
            if (known == probe.Fingerprint) Console.WriteLine("identique à l'empreinte enregistrée.");
            else if (known is null && probe.TrustedByWindows) Console.WriteLine("certificat reconnu par Windows.");
            else
            {
                Console.WriteLine();
                Console.WriteLine(known is null
                    ? "Le certificat de Mailflow n'est pas reconnu par Windows (normal : Mailflow crée lui-même son certificat)."
                    : "ATTENTION : l'empreinte du certificat de Mailflow a changé depuis la dernière configuration.");
                Console.WriteLine("Empreinte SHA-256 : " + AgentConfig.FormatFingerprint(probe.Fingerprint));
                Console.WriteLine("Elle doit être identique à celle affichée dans Mailflow (Imprimantes > Agents d'impression).");
                Console.Write("Faire confiance à ce certificat ? (o/n) : ");
                if (Console.ReadLine()?.Trim().StartsWith("o", StringComparison.OrdinalIgnoreCase) != true) { Console.WriteLine("Abandon."); return 1; }
                config.ServerCertSha256 = probe.Fingerprint;
            }
        }

        Console.Write("Test de la connexion... ");
        try
        {
            using var http = config.NewHttpClient();
            using var r = await http.GetAsync("api/agent/ping");
            if (r.StatusCode == HttpStatusCode.Unauthorized) { Console.WriteLine("nom ou mot de passe refusé par Mailflow."); return 1; }
            r.EnsureSuccessStatusCode();
            var name = (await r.Content.ReadFromJsonAsync<Dictionary<string, string>>())?.GetValueOrDefault("name");
            Console.WriteLine($"OK (agent « {name} »).");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Console.WriteLine("échec : " + ex.Message);
            return 1;
        }

        config.Save(AgentConfig.DefaultPath);
        Console.WriteLine("Configuration enregistrée dans " + AgentConfig.DefaultPath);

        if (OperatingSystem.IsWindows())
        {
            Console.Write("Démarrer l'agent avec Windows ? (o/n) : ");
            if (Console.ReadLine()?.Trim().StartsWith("o", StringComparison.OrdinalIgnoreCase) == true)
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                key.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --run");
                Console.WriteLine("Démarrage automatique activé.");
            }
        }
        Console.WriteLine("\nTerminé. Lancez MailflowPrintAgent.exe pour démarrer l'agent maintenant.");
        return 0;
    }

    private static int Uninstall()
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(RunValue, throwOnMissingValue: false);
        }
        Console.WriteLine("Démarrage automatique désactivé. Supprimez le dossier pour retirer l'agent.");
        return 0;
    }

    private static string ReadHidden()
    {
        var sb = new System.Text.StringBuilder();
        while (Console.ReadKey(intercept: true) is var k && k.Key != ConsoleKey.Enter)
        {
            if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) { sb.Length--; Console.Write("\b \b"); } }
            else if (!char.IsControl(k.KeyChar)) { sb.Append(k.KeyChar); Console.Write('*'); }
        }
        Console.WriteLine();
        return sb.ToString();
    }

    private static void Log(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
        try
        {
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1_000_000) File.Move(LogPath, LogPath + ".old", overwrite: true);
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch (IOException) { /* logging must never stop printing */ }
        try { Console.WriteLine(line); } catch (IOException) { /* console already released in --run mode */ }
    }
}
