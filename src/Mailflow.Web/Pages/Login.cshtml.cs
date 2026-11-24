using System.Security.Claims;
using Mailflow.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Mailflow.Web.Pages;

[AllowAnonymous]
public class LoginModel(IAdminPasswordStore store, IPasswordHasher<object> hasher) : PageModel
{
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string? ReturnUrl { get; set; }
    public string? Error { get; private set; }

    public void OnGet(string? returnUrl = null) => ReturnUrl = returnUrl;

    public async Task<IActionResult> OnPostAsync()
    {
        var hash = await store.GetHashAsync(HttpContext.RequestAborted);
        if (string.IsNullOrEmpty(hash)) return RedirectToPage("/Setup");

        var result = hasher.VerifyHashedPassword(new object(), hash, Password ?? "");
        if (result == PasswordVerificationResult.Failed)
        {
            Error = "Mot de passe incorrect.";
            return Page();
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/");
    }
}
