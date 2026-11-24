using System.ComponentModel.DataAnnotations;
using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Web.Pages.Mailboxes;

public class EditModel(IAppDbContext db, ISecretStore secrets, IMailClient mail) : PageModel
{
    public class MailboxInput
    {
        public int StoreId { get; set; }
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
        /// <summary>Write-only. Blank on edit = keep the stored password.</summary>
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

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        await LoadStores();
        if (id is null) return Page();
        var m = await db.Mailboxes.FindAsync(id.Value);
        if (m is null) return NotFound();
        Input = new MailboxInput
        {
            StoreId = m.StoreId, DisplayName = m.DisplayName, Email = m.Email, Host = m.Host, Port = m.Port, UseSsl = m.UseSsl,
            Username = m.Username, InboxFolder = m.InboxFolder, CheckIntervalMinutes = m.CheckIntervalMinutes,
            LookbackDays = m.LookbackDays, DefaultAfterProcessing = m.DefaultAfterProcessing, IsActive = m.IsActive,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
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
        if (id is null && string.IsNullOrEmpty(Input.Password)) ModelState.AddModelError("Input.Password", "Le mot de passe est requis.");
        if (!await db.Stores.AnyAsync(s => s.Id == Input.StoreId)) ModelState.AddModelError("Input.StoreId", "Magasin invalide.");
        if (!ModelState.IsValid) { await LoadStores(); return Page(); }

        Mailbox? m = null;
        if (id is not null)
        {
            m = await db.Mailboxes.FindAsync(id.Value);
            if (m is null) return NotFound();
        }
        else db.Mailboxes.Add(m = new Mailbox());
        m.StoreId = Input.StoreId;
        m.DisplayName = Input.DisplayName;
        m.Email = Input.Email;
        m.Host = Input.Host;
        m.Port = Input.Port;
        m.UseSsl = Input.UseSsl;
        m.Username = Input.Username;
        if (!string.IsNullOrEmpty(Input.Password)) m.EncryptedPassword = secrets.Protect(Input.Password);
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
            var password = Input.Password;
            if (string.IsNullOrEmpty(password) && id is not null)
            {
                var stored = await db.Mailboxes.Where(x => x.Id == id.Value).Select(x => x.EncryptedPassword).FirstOrDefaultAsync(ct);
                if (!string.IsNullOrEmpty(stored)) password = secrets.Unprotect(stored);
            }
            if (string.IsNullOrWhiteSpace(Input.Host) || Input.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(Input.Username) || string.IsNullOrEmpty(password))
                result = new ConnectionTestResult(false, 0, [], "Renseignez serveur, port, identifiant et mot de passe.");
            else
                result = await mail.TestConnectionAsync(new MailboxConnection(Input.Host.Trim(), Input.Port, Input.UseSsl, Input.Username.Trim(), password), ct);
        }
        catch (Exception ex)
        {
            result = new ConnectionTestResult(false, 0, [], ex.Message);
        }
        return Partial("_TestResult", result);
    }

    private async Task LoadStores() => Stores = await db.Stores.OrderBy(s => s.Name).ToListAsync();
}
