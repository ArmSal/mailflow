using Mailflow.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Mailflow.Web.Pages;

[AllowAnonymous]
public class SetupModel(IAdminPasswordStore store, IPasswordHasher<object> hasher) : PageModel
{
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string Confirm { get; set; } = "";
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
        => await IsConfiguredAsync() ? RedirectToPage("/Login") : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        // Setup is only possible while no password exists.
        if (await IsConfiguredAsync()) return RedirectToPage("/Login");

        if ((Password ?? "").Length < 8) Error = "Le mot de passe doit contenir au moins 8 caractères.";
        else if (Password != Confirm) Error = "Les deux mots de passe ne correspondent pas.";
        if (Error is not null) return Page();

        await store.SetHashAsync(hasher.HashPassword(new object(), Password!), HttpContext.RequestAborted);
        return RedirectToPage("/Login");
    }

    private async Task<bool> IsConfiguredAsync()
        => !string.IsNullOrEmpty(await store.GetHashAsync(HttpContext.RequestAborted));
}
