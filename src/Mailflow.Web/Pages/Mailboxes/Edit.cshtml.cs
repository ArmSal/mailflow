using System.ComponentModel.DataAnnotations;
using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Web.Pages.Mailboxes;

/// <summary>What the Microsoft sign-in panel shows. Never carries tokens or caches.</summary>
public sealed record MsSignInPanel(int MailboxId, bool SignedIn, OAuthSignInStatus Status, bool Configured);

public class EditModel(IAppDbContext db, ISecretStore secrets, IMailClient mail, IOAuthSignInService oauth, IMailboxConnectionFactory connections) : PageModel
{
    public class MailboxInput
    {
        public int StoreId { get; set; }
        public AuthMethod AuthMethod { get; set; } = AuthMethod.Password;
        [Required(ErrorMessage = "Le nom est requis."), StringLength(100)]
        public string DisplayName { get; set; } = "";
        [Required(ErrorMessage = "L'adresse est requise."), EmailAddress(ErrorMessage = "Adresse invalide."), StringLength(254)]
        public string Email { get; set; } = "";
        [Required(ErrorMessage = "Le serveur est requis."), StringLength(255)]
        public string Host { get; set; } = "";
        [Range(1, 65535, ErrorMessage = "Port : 1 à 65535.")]
        public int Port { get; set; } = 993;
        public bool UseSsl { get; set; } = true;
        [Required(ErrorMessage = "L'identifiant est requis."), StringLength(254)]
        public string Username { get; set; } = "";
        /// <summary>Write-only. Blank on edit = keep the stored password. Ignored in Microsoft mode.</summary>
        public string? Password { get; set; }
        [Required, StringLength(200)]
        public string InboxFolder { get; set; } = "INBOX";
        [Range(1, 1440, ErrorMessage = "Intervalle : 1 à 1440 minutes.")]
        public int CheckIntervalMinutes { get; set; } = 5;
        [Range(1, 365, ErrorMessage = "Historique : 1 à 365 jours.")]
        public int LookbackDays { get; set; } = 3;
        public AfterProcessing DefaultAfterProcessing { get; set; } = AfterProcessing.Leave;
        public bool IsActive { get; set; } = true;
    }

    [BindProperty] public MailboxInput Input { get; set; } = new();
    public List<Store> Stores { get; private set; } = [];
    public MsSignInPanel MsPanel { get; private set; } = new(0, false, new OAuthSignInStatus(OAuthSignInState.None), false);

    public async Task<IActionResult> OnGetAsync(int? id, CancellationToken ct = default)
    {
        await LoadStores();
        if (id is null) { await LoadPanel(null, ct); return Page(); }
        var m = await db.Mailboxes.FindAsync(id.Value);
        if (m is null) return NotFound();
        await LoadPanel(id, ct);
        Input = new MailboxInput
        {
            StoreId = m.StoreId, AuthMethod = m.AuthMethod, DisplayName = m.DisplayName, Email = m.Email, Host = m.Host, Port = m.Port, UseSsl = m.UseSsl,
            Username = m.Username, InboxFolder = m.InboxFolder, CheckIntervalMinutes = m.CheckIntervalMinutes,
            LookbackDays = m.LookbackDays, DefaultAfterProcessing = m.DefaultAfterProcessing, IsActive = m.IsActive,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id, CancellationToken ct = default)
    {
        Input.DisplayName = (Input.DisplayName ?? "").Trim();
        Input.Email = (Input.Email ?? "").Trim();
        Input.Host = (Input.Host ?? "").Trim();
        Input.Username = (Input.Username ?? "").Trim();
        Input.InboxFolder = string.IsNullOrWhiteSpace(Input.InboxFolder) ? "INBOX" : Input.InboxFolder.Trim();
        if (Input.DisplayName.Length == 0) ModelState.AddModelError("Input.DisplayName", "Le nom est requis.");
        if (Input.Email.Length == 0 || !new EmailAddressAttribute().IsValid(Input.Email)) ModelState.AddModelError("Input.Email", "Adresse invalide.");
        if (Input.Host.Length == 0) ModelState.AddModelError("Input.Host", "Le serveur est requis.");
        if (Input.Username.Length == 0) ModelState.AddModelError("Input.Username", "L'identifiant est requis.");
        if (Input.Port is < 1 or > 65535) ModelState.AddModelError("Input.Port", "Port : 1 à 65535.");
        if (Input.CheckIntervalMinutes is < 1 or > 1440) ModelState.AddModelError("Input.CheckIntervalMinutes", "Intervalle : 1 à 1440 minutes.");
        if (Input.LookbackDays is < 1 or > 365) ModelState.AddModelError("Input.LookbackDays", "Historique : 1 à 365 jours.");
        if (!Enum.IsDefined(Input.DefaultAfterProcessing)) ModelState.AddModelError("Input.DefaultAfterProcessing", "Valeur invalide.");
        if (!Enum.IsDefined(Input.AuthMethod)) ModelState.AddModelError("Input.AuthMethod", "Valeur invalide.");
        if (!await db.Stores.AnyAsync(s => s.Id == Input.StoreId)) ModelState.AddModelError("Input.StoreId", "Magasin invalide.");

        Mailbox? m = null;
        if (id is not null)
        {
            m = await db.Mailboxes.FindAsync(id.Value);
            if (m is null) return NotFound();
        }
        if (Input.AuthMethod == AuthMethod.Password && string.IsNullOrEmpty(Input.Password) && string.IsNullOrEmpty(m?.EncryptedPassword))
            ModelState.AddModelError("Input.Password", "Le mot de passe est requis.");
        if (!ModelState.IsValid) { await LoadStores(); await LoadPanel(id, ct); return Page(); }

        if (m is null) db.Mailboxes.Add(m = new Mailbox());
        m.StoreId = Input.StoreId;
        m.AuthMethod = Input.AuthMethod;
        m.DisplayName = Input.DisplayName;
        m.Email = Input.Email;
        m.Host = Input.Host;
        m.Port = Input.Port;
        m.UseSsl = Input.UseSsl;
        m.Username = Input.Username;
        if (Input.AuthMethod == AuthMethod.Password && !string.IsNullOrEmpty(Input.Password)) m.EncryptedPassword = secrets.Protect(Input.Password);
        m.InboxFolder = Input.InboxFolder;
        m.CheckIntervalMinutes = Input.CheckIntervalMinutes;
        m.LookbackDays = Input.LookbackDays;
        m.DefaultAfterProcessing = Input.DefaultAfterProcessing;
        m.IsActive = Input.IsActive;
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostTestAsync(int? id, CancellationToken ct)
    {
        ConnectionTestResult result;
        try
        {
            var stored = id is null ? null : await db.Mailboxes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id.Value, ct);
            var oauthMode = Input.AuthMethod == AuthMethod.MicrosoftOAuth;
            var password = oauthMode ? null : Input.Password;
            if (!oauthMode && string.IsNullOrEmpty(password) && !string.IsNullOrEmpty(stored?.EncryptedPassword)) password = secrets.Unprotect(stored.EncryptedPassword);

            if (string.IsNullOrWhiteSpace(Input.Host) || Input.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(Input.Username))
                result = new ConnectionTestResult(false, 0, [], oauthMode ? "Renseignez serveur, port et identifiant." : "Renseignez serveur, port, identifiant et mot de passe.");
            else if (oauthMode && stored is null)
                result = new ConnectionTestResult(false, 0, [], "Enregistrez d'abord la boîte, puis connectez-la avec Microsoft.");
            else if (!oauthMode && string.IsNullOrEmpty(password))
                result = new ConnectionTestResult(false, 0, [], "Renseignez serveur, port, identifiant et mot de passe.");
            else
            {
                var probe = new Mailbox
                {
                    Id = stored?.Id ?? 0, Host = Input.Host.Trim(), Port = Input.Port, UseSsl = Input.UseSsl, Username = Input.Username.Trim(),
                    AuthMethod = Input.AuthMethod, EncryptedPassword = password is null ? "" : secrets.Protect(password),
                    EncryptedOAuthCache = stored?.EncryptedOAuthCache,
                };
                var connection = await connections.CreateAsync(probe, ct);
                result = await mail.TestConnectionAsync(connection, ct);
            }
        }
        catch (Exception ex)
        {
            // OAuthReconnectRequiredException carries a user-facing message: same alert style as any failure.
            result = new ConnectionTestResult(false, 0, [], ex.Message);
        }
        return this.PartialWith("_TestResult", result, ("Hint", result.Success ? null : MailErrorHints.For(result.Error)));
    }

    public async Task<IActionResult> OnPostMsStartAsync(int id, CancellationToken ct)
    {
        if (!await db.Mailboxes.AnyAsync(x => x.Id == id, ct)) return NotFound();
        var configured = await oauth.IsConfiguredAsync(ct);
        var status = configured
            ? await oauth.StartAsync(id, ct)
            : new OAuthSignInStatus(OAuthSignInState.Failed, null, "Renseignez d'abord l'identifiant d'application Microsoft dans les Paramètres.");
        return await PanelAsync(id, status, configured, ct);
    }

    public async Task<IActionResult> OnGetMsStatusAsync(int id, CancellationToken ct)
        => await PanelAsync(id, oauth.GetStatus(id), await oauth.IsConfiguredAsync(ct), ct);

    public async Task<IActionResult> OnPostMsSignOutAsync(int id, CancellationToken ct)
    {
        if (!await db.Mailboxes.AnyAsync(x => x.Id == id, ct)) return NotFound();
        await oauth.SignOutAsync(id, ct);
        return Partial("_MsSignIn", new MsSignInPanel(id, false, new OAuthSignInStatus(OAuthSignInState.None), await oauth.IsConfiguredAsync(ct)));
    }

    private async Task<IActionResult> PanelAsync(int id, OAuthSignInStatus status, bool configured, CancellationToken ct)
    {
        var m = await db.Mailboxes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return NotFound();
        return Partial("_MsSignIn", new MsSignInPanel(id, status.State == OAuthSignInState.Succeeded || oauth.IsSignedIn(m), status, configured));
    }

    private async Task LoadPanel(int? id, CancellationToken ct)
    {
        var configured = await oauth.IsConfiguredAsync(ct);
        var m = id is null ? null : await db.Mailboxes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        var status = m is null ? new OAuthSignInStatus(OAuthSignInState.None) : oauth.GetStatus(m.Id);
        MsPanel = new MsSignInPanel(m?.Id ?? 0, m is not null && (status.State == OAuthSignInState.Succeeded || oauth.IsSignedIn(m)), status, configured);
    }

    private async Task LoadStores() => Stores = await db.Stores.OrderBy(s => s.Name).ToListAsync();
}
