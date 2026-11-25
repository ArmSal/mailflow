using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Mailflow.Web.Localization;

public static class LocalizationSetup
{
    public const string CookieName = ".Mailflow.Culture";

    /// <summary>French by default; only the language cookie changes it (the browser's Accept-Language is ignored).</summary>
    public static IApplicationBuilder UseMailflowLocalization(this IApplicationBuilder app) =>
        app.UseRequestLocalization(o =>
        {
            o.SetDefaultCulture(Loc.French).AddSupportedCultures(Loc.French, Loc.English).AddSupportedUICultures(Loc.French, Loc.English);
            o.RequestCultureProviders = [new CookieRequestCultureProvider { CookieName = CookieName }];
        });

    /// <summary>GET /language/en?returnUrl=/Settings : remembers the language in a cookie and goes back.</summary>
    public static IEndpointRouteBuilder MapLanguageSwitch(this IEndpointRouteBuilder app)
    {
        app.MapGet("/language/{culture}", (string culture, string? returnUrl, HttpContext ctx) =>
        {
            if (culture is Loc.French or Loc.English)
                ctx.Response.Cookies.Append(CookieName, CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                    new CookieOptions { MaxAge = TimeSpan.FromDays(365), HttpOnly = true, SameSite = SameSiteMode.Lax, IsEssential = true });
            var target = !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\") ? returnUrl : "/";
            return Results.LocalRedirect(target);
        }).AllowAnonymous();
        return app;
    }
}

/// <summary>Translates validation messages (DataAnnotations and the ones added by handlers before binding ends) on English requests.</summary>
public sealed class TranslateModelStateFilter : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        if (Loc.IsEnglish)
            foreach (var entry in context.ModelState.Values)
            {
                if (entry.Errors.Count == 0) continue;
                var messages = entry.Errors.Select(e => e.ErrorMessage).ToList();
                entry.Errors.Clear();
                foreach (var m in messages) entry.Errors.Add(string.IsNullOrEmpty(m) ? m : Loc.T(m));
            }
        return next();
    }
}
