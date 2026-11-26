using System.ComponentModel.DataAnnotations;
using Mailflow.Application.Abstractions;
using Mailflow.Domain.Entities;
using Mailflow.Domain.Enums;
using Mailflow.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Mailflow.Web.Pages.Mailboxes;

public class EditModel(IAppDbContext db, ISecretStore secrets, IMailClient mail, IMailboxConnectionFactory connections) : PageModel
{
    public class MailboxInput
    {
        [Required(ErrorMessage = "Le nom est requis."), StringLength(100)]
        public string DisplayName { get; set; } = "";
        [Required(ErrorMessage = "L'adresse est requise."), EmailAddress(ErrorMessage = "Adresse invalide."), StringLength(254)]
        public string Email { get; set; } = "";
        [Required(ErrorMessage = "Le serveur est requis."), StringLength(255)]
        public string Host { get; set; } = "";
        [Range(1, 65535, ErrorMessage = "Port : 1 à 65535.")]
        public int Port { get; set; } = 993;
        public bool UseSsl { get; set; } = true;
        [StringLength(254)]
        public string? Username { get; set; }
        /// <summary>Write-only. Blank on edit = keep the stored password.</summary>
        public string? Password { get; set; }
        [Required, StringLength(200)]
        public string InboxFolder { get; set; } = "INBOX";
        [Range(1, 1440, ErrorMessage = "Intervalle : 1 à 1440 minutes.")]
        public int CheckIntervalMinutes { get; set; } = 5;
        [DataType(DataType.Date)]
        public DateTime SortFromDate { get; set; } = DateTime.UtcNow.Date.AddDays(-3);
        [Range(1, 7, ErrorMessage = "Impression : 1 à 7 jours.")]
        public int PrintMaxDays { get; set; } = 1;
        public AfterProcessing DefaultAfterProcessing { get; set; } = AfterProcessing.Leave;
        public bool IsActive { get; set; } = true;
    }

    [BindProperty] public MailboxInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int? id, CancellationToken ct = default)
    {
        if (id is null) return Page();
        var m = await db.Mailboxes.FindAsync(id.Value);
        if (m is null) return NotFound();
        Input = new MailboxInput
        {
            DisplayName = m.DisplayName, Email = m.Email, Host = m.Host, Port = m.Port, UseSsl = m.UseSsl,
            Username = m.Username, InboxFolder = m.InboxFolder, CheckIntervalMinutes = m.CheckIntervalMinutes,
            SortFromDate = m.SortFromDate, PrintMaxDays = m.PrintMaxDays, DefaultAfterProcessing = m.DefaultAfterProcessing, IsActive = m.IsActive,
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
        if (Input.DisplayName.Length == 0) ModelState.AddModelError("Input.DisplayName", Loc.T("Le nom est requis."));
        if (Input.Email.Length == 0 || !new EmailAddressAttribute().IsValid(Input.Email)) ModelState.AddModelError("Input.Email", Loc.T("Adresse invalide."));
        if (Input.Host.Length == 0) ModelState.AddModelError("Input.Host", Loc.T("Le serveur est requis."));
        if (Input.Username.Length == 0) Input.Username = Input.Email;
        if (Input.Port is < 1 or > 65535) ModelState.AddModelError("Input.Port", Loc.T("Port : 1 à 65535."));
        if (Input.CheckIntervalMinutes is < 1 or > 1440) ModelState.AddModelError("Input.CheckIntervalMinutes", Loc.T("Intervalle : 1 à 1440 minutes."));
        if (Input.SortFromDate.Date > DateTime.UtcNow.Date) ModelState.AddModelError("Input.SortFromDate", Loc.T("La date de tri ne peut pas être dans le futur."));
        if (Input.PrintMaxDays is < 1 or > 7) ModelState.AddModelError("Input.PrintMaxDays", Loc.T("Impression : 1 à 7 jours."));
        if (!Enum.IsDefined(Input.DefaultAfterProcessing)) ModelState.AddModelError("Input.DefaultAfterProcessing", Loc.T("Valeur invalide."));

        Mailbox? m = null;
        if (id is not null)
        {
            m = await db.Mailboxes.FindAsync(id.Value);
            if (m is null) return NotFound();
        }
        if (string.IsNullOrEmpty(Input.Password) && string.IsNullOrEmpty(m?.EncryptedPassword))
            ModelState.AddModelError("Input.Password", Loc.T("Le mot de passe est requis."));
        if (!ModelState.IsValid) return Page();

        if (m is null) db.Mailboxes.Add(m = new Mailbox());
        m.DisplayName = Input.DisplayName;
        m.Email = Input.Email;
        m.Host = Input.Host;
        m.Port = Input.Port;
        m.UseSsl = Input.UseSsl;
        m.Username = Input.Username;
        if (!string.IsNullOrEmpty(Input.Password)) m.EncryptedPassword = secrets.Protect(Input.Password);
        m.LastError = null;
        m.InboxFolder = Input.InboxFolder;
        m.CheckIntervalMinutes = Input.CheckIntervalMinutes;
        m.SortFromDate = DateTime.SpecifyKind(Input.SortFromDate.Date, DateTimeKind.Utc);
        m.PrintMaxDays = Input.PrintMaxDays;
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
            var username = string.IsNullOrWhiteSpace(Input.Username) ? Input.Email : Input.Username;
            var password = Input.Password;
            if (string.IsNullOrEmpty(password) && !string.IsNullOrEmpty(stored?.EncryptedPassword)) password = secrets.Unprotect(stored.EncryptedPassword);

            if (string.IsNullOrWhiteSpace(Input.Host) || Input.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
                result = new ConnectionTestResult(false, 0, [], Loc.T("Renseignez l'adresse e-mail et le mot de passe (et le serveur sous « Réglages du serveur »)."));
            else
            {
                var probe = new Mailbox
                {
                    Id = stored?.Id ?? 0, Host = Input.Host.Trim(), Port = Input.Port, UseSsl = Input.UseSsl, Username = username.Trim(),
                    EncryptedPassword = secrets.Protect(password),
                };
                var connection = await connections.CreateAsync(probe, ct);
                result = await mail.TestConnectionAsync(connection, ct);
            }
        }
        catch (Exception ex)
        {
            result = new ConnectionTestResult(false, 0, [], ex.Message);
        }
        return this.PartialWith("_TestResult", result, ("Hint", result.Success ? null : MailErrorHints.For(result.Error, Input.Host)));
    }
}
