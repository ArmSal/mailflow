namespace Mailflow.Desktop;

/// <summary>Command-line switches of the desktop shell. Other arguments are passed on to the web host.</summary>
public sealed record LaunchOptions(bool Minimized, bool Headless, string[] HostArgs)
{
    public static LaunchOptions Parse(string[] args)
    {
        bool Is(string a, string name) => string.Equals(a, name, StringComparison.OrdinalIgnoreCase);
        return new LaunchOptions(
            args.Any(a => Is(a, "--minimized")),
            args.Any(a => Is(a, "--headless")),
            args.Where(a => !Is(a, "--minimized") && !Is(a, "--headless")).ToArray());
    }
}
