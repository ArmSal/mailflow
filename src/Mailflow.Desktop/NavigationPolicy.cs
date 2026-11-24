namespace Mailflow.Desktop;

/// <summary>The window may only show the dashboard's own origin; everything else goes to the default browser.</summary>
public static class NavigationPolicy
{
    public static bool IsAllowed(string? uri, Uri origin) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var u)
        && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)
        && u.Scheme == origin.Scheme && u.Port == origin.Port
        && string.Equals(u.Host, origin.Host, StringComparison.OrdinalIgnoreCase);

    public static bool IsExternalWeb(string? uri, Uri origin) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var u)
        && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)
        && !IsAllowed(uri, origin);
}
