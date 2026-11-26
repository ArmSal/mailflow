using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mailflow.Agent;

/// <summary>Settings kept in agent.json next to the program. The password is stored encrypted for the current Windows user.</summary>
public sealed class AgentConfig
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string ServerUrl { get; set; } = "";
    /// <summary>The name this agent has in Mailflow (Printers > Print agents); with the password it identifies the agent.</summary>
    public string AgentName { get; set; } = "";
    /// <summary>The password, encrypted with Windows DPAPI for the current user (written by --setup).</summary>
    public string? PasswordProtected { get; set; }
    /// <summary>Plain-text alternative for manual setups; --setup replaces it with the encrypted form.</summary>
    public string? Password { get; set; }
    public string SumatraPath { get; set; } = "SumatraPDF.exe";
    public int PollSeconds { get; set; } = 5;

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "agent.json");

    [JsonIgnore]
    public string? ResolvedPassword =>
        !string.IsNullOrEmpty(PasswordProtected) && OperatingSystem.IsWindows()
            ? Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(PasswordProtected), null, DataProtectionScope.CurrentUser))
            : Password;

    public void SetPassword(string password)
    {
        if (OperatingSystem.IsWindows())
        {
            PasswordProtected = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser));
            Password = null;
        }
        else Password = password;
    }

    /// <summary>The first problem found, or null when the configuration can be used.</summary>
    public string? Validate()
    {
        if (!Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return "ServerUrl doit être une adresse http:// ou https:// complète.";
        if (string.IsNullOrWhiteSpace(AgentName) || AgentName.Trim().Length > 100) return "AgentName est requis (le nom de cet agent dans Mailflow, 100 caractères maximum).";
        string? password;
        try { password = ResolvedPassword; }
        catch (CryptographicException) { return "Le mot de passe chiffré ne peut pas être lu (autre utilisateur Windows ?). Relancez --setup."; }
        if (string.IsNullOrWhiteSpace(password)) return "Aucun mot de passe configuré. Lancez --setup.";
        if (PollSeconds is < 1 or > 300) return "PollSeconds doit être compris entre 1 et 300.";
        return null;
    }

    public Uri BaseAddress => new(ServerUrl.TrimEnd('/') + "/");

    /// <summary>An HTTP client for Mailflow's agent API: every request carries the agent name and password.</summary>
    public HttpClient NewHttpClient(HttpMessageHandler? handler = null)
    {
        var http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.BaseAddress = BaseAddress;
        http.Timeout = TimeSpan.FromSeconds(60);
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ResolvedPassword);
        // URL-encoded: a name such as "Entrepôt" is not safe as a raw header value; Mailflow decodes it.
        http.DefaultRequestHeaders.Add("X-Agent-Name", Uri.EscapeDataString(AgentName.Trim()));
        return http;
    }

    public static AgentConfig? Load(string path)
        => File.Exists(path) ? JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(path)) : null;

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
}
