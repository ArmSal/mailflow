using System.Globalization;
using System.Runtime.CompilerServices;

namespace Mailflow.Integration.Tests;

internal static class TestCulture
{
    // Mailflow's source language is French and many tests assert French texts. Pin the culture so the result
    // does not depend on the language of the machine running them (a GitHub runner is English).
    [ModuleInitializer]
    internal static void Init()
    {
        var fr = new CultureInfo("fr");
        CultureInfo.DefaultThreadCurrentUICulture = fr;
        CultureInfo.CurrentUICulture = fr;
    }
}
