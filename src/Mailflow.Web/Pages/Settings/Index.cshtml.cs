using System.ComponentModel.DataAnnotations;
using Mailflow.Application.Abstractions;
using Mailflow.Domain.Enums;
using Mailflow.Infrastructure.Storage;
using Mailflow.Web.Auth;
using Mailflow.Web.Backup;
using Mailflow.Web.Localization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Mailflow.Web.Pages.Settings;

public class IndexModel(
    IAppDbContext db,
    IAdminPasswordStore passwords,
    IPasswordHasher<object> hasher,
    BackupService backup,
    IOptions<DocumentStoreOptions> storeOptions) : PageModel
{
    public class SettingsInput
    {
        [Required(ErrorMessage = "Le dossier des documents est requis.")]
        public string DocumentsRoot { get; set; } = "";
        [Required(ErrorMessage = "Le modèle de nom de fichier est requis.")]
        public string FileNamePattern { get; set; } = "";
        public bool PrintEmailBodyWithoutPdf { get; set; }
    }

    public class PasswordInput
    {
        [DataType(DataType.Password), Required(ErrorMessage = "Le mot de passe actuel est requis.")]
        public string Current { get; set; } = "";
        [DataType(DataType.Password), Required(ErrorMessage = "Le nouveau mot de passe est requis.")]
        public string New { get; set; } = "";
        [DataType(DataType.Password), Required(ErrorMessage = "La confirmation est requise.")]
        public string Confirm { get; set; } = "";
    }

    public class SenderCheckInput
    {
        public string? Mode { get; set; }
    }

    [BindProperty] public SenderCheckInput SenderCheck { get; set; } = new();
    public string? SenderCheckMessage { get; private set; }
    [BindProperty] public SettingsInput Input { get; set; } = new();
    [BindProperty] public PasswordInput Password { get; set; } = new();
    public string? SettingsMessage { get; private set; }
    public string? PasswordMessage { get; private set; }
    public bool PasswordOk { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    public IActionResult OnGetBrowse(string? path) => Partial("_FolderBrowser", FolderBrowser.List(path));

    public async Task<IActionResult> OnPostSettingsAsync()
    {
        var ct = HttpContext.RequestAborted;
        await LoadSecondarySectionsAsync(ct);
        Input.DocumentsRoot = (Input.DocumentsRoot ?? "").Trim();
        Input.FileNamePattern = (Input.FileNamePattern ?? "").Trim();
        var errors = new (string Field, string? Error)[]
        {
            ("Input.DocumentsRoot", SettingsRules.ValidateRoot(Input.DocumentsRoot)),
            ("Input.FileNamePattern", SettingsRules.ValidatePattern(Input.FileNamePattern)),
        };
        foreach (var (field, error) in errors.Where(e => e.Error is not null)) ModelState.AddModelError(field, error!);
        if (!ModelState.IsValid) return Page();

        await SettingsRules.SaveAsync(db, SettingsRules.RootKey, Input.DocumentsRoot, ct);
        await SettingsRules.SaveAsync(db, SettingsRules.PatternKey, Input.FileNamePattern, ct);
        await SettingsRules.SaveAsync(db, SettingsRules.PrintEmailBodyKey, Input.PrintEmailBodyWithoutPdf ? "true" : "false", ct);
        await db.SaveChangesAsync(ct);
        SettingsMessage = Loc.T("Paramètres enregistrés.");
        return Page();
    }

    public async Task<IActionResult> OnPostSenderCheckAsync()
    {
        var ct = HttpContext.RequestAborted;
        var mode = (SenderCheck.Mode ?? "").Trim();
        await LoadAsync();
        if (SettingsRules.ValidateSenderCheck(mode) is { } error)
        {
            ModelState.AddModelError("SenderCheck.Mode", error);
            return Page();
        }
        await SettingsRules.SaveAsync(db, SettingsRules.SenderCheckKey, mode, ct);
        await db.SaveChangesAsync(ct);
        SenderCheck = new SenderCheckInput { Mode = mode };
        SenderCheckMessage = Loc.T("Vérification de l'expéditeur enregistrée.");
        ModelState.Clear();
        return Page();
    }

    public async Task<IActionResult> OnPostPasswordAsync()
    {
        var ct = HttpContext.RequestAborted;
        await LoadAsync();
        var hash = await passwords.GetHashAsync(ct);
        if (string.IsNullOrEmpty(hash) || hasher.VerifyHashedPassword(new object(), hash, Password.Current ?? "") == PasswordVerificationResult.Failed)
            PasswordMessage = Loc.T("Le mot de passe actuel est incorrect.");
        else if (SettingsRules.ValidateNewPassword(Password.New, Password.Confirm) is { } error)
            PasswordMessage = error;
        else
        {
            await passwords.SetHashAsync(hasher.HashPassword(new object(), Password.New), ct);
            PasswordOk = true;
            PasswordMessage = Loc.T("Mot de passe modifié.");
        }
        Password = new();
        ModelState.Clear();
        return Page();
    }

    public async Task<IActionResult> OnPostBackupAsync()
    {
        var r = await backup.RunAsync(HttpContext.RequestAborted);
        return Partial("_BackupResult", r);
    }

    private async Task LoadAsync()
    {
        var s = await SettingsRules.LoadAsync(db, HttpContext.RequestAborted);
        Input = new SettingsInput
        {
            DocumentsRoot = s.GetValueOrDefault(SettingsRules.RootKey) is { Length: > 0 } r ? r : storeOptions.Value.DocumentsRoot,
            FileNamePattern = s.GetValueOrDefault(SettingsRules.PatternKey) is { Length: > 0 } p ? p : storeOptions.Value.FileNamePattern,
            PrintEmailBodyWithoutPdf = SettingsRules.ParsePrintEmailBody(s.GetValueOrDefault(SettingsRules.PrintEmailBodyKey)),
        };
        SetSenderCheck(s);
    }

    private async Task LoadSecondarySectionsAsync(CancellationToken ct)
    {
        var s = await SettingsRules.LoadAsync(db, ct);
        SetSenderCheck(s);
    }

    private void SetSenderCheck(Dictionary<string, string> s)
        => SenderCheck = new SenderCheckInput { Mode = SettingsRules.ParseSenderCheck(s.GetValueOrDefault(SettingsRules.SenderCheckKey)).ToString() };
}
