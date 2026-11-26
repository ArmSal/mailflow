using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Mailflow.Domain.Tests;

internal static class RegexWarmUp
{
    // Mailflow refuses a regex that takes more than 100 ms (a protection against runaway patterns). The first regex of
    // a test run also pays for runtime start-up, which on a busy CI runner can exceed that and make a correct test fail
    // at random. Warm the engine up, with the options the rules use, before any test runs.
    [ModuleInitializer]
    internal static void Run()
    {
        foreach (var options in new[] { RegexOptions.None, RegexOptions.IgnoreCase })
            _ = new Regex(@"^\d+_a|b\s*(c)$", options, TimeSpan.FromSeconds(10)).IsMatch("12_a b c");
    }
}
