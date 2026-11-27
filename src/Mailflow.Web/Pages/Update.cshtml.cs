using Mailflow.Application.Updates;
using Mailflow.Web.Localization;
using Mailflow.Web.Updates;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Mailflow.Web.Pages;

public class UpdateModel(UpdateChecker checker, UpdateInstaller installer) : PageModel
{
    public string? Error { get; private set; }
    public bool Started { get; private set; }

    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!checker.InstallEnabled || checker.Available is not { } release || !UpdateInstaller.IsSetupInstall())
        {
            Error = Loc.T("L'installation en un clic n'est pas disponible pour cette installation.");
            return Page();
        }
        var r = await installer.PrepareAsync(release.Tag, ct);
        if (r.Error != PrepareError.None)
        {
            Error = Message(r.Error);
            return Page();
        }
        // Launch once the answer is sent: Setup stops this process.
        var path = r.InstallerPath!;
        Response.OnCompleted(() => { UpdateInstaller.Launch(path); return Task.CompletedTask; });
        Started = true;
        return Page();
    }

    private static string Message(PrepareError e) => e switch
    {
        PrepareError.NoManifest => Loc.T("Cette version n'est pas encore signée : réessayez plus tard ou installez-la à la main depuis la page de la version."),
        PrepareError.BadSignature or PrepareError.HashMismatch or PrepareError.FileMissing => Loc.T("La vérification de la signature a échoué : la mise à jour est refusée. Installez-la à la main depuis la page de la version."),
        PrepareError.NotNewer => Loc.T("Vous utilisez déjà cette version ou une version plus récente."),
        PrepareError.BackupFailed => Loc.T("La sauvegarde de la base de données a échoué : la mise à jour n'a pas été lancée."),
        _ => Loc.T("Le téléchargement a échoué. Vérifiez la connexion Internet et réessayez."),
    };
}
