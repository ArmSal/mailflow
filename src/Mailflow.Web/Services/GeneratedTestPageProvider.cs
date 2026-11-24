using Mailflow.Web.Pages.Printers;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Mailflow.Web.Services;

/// <summary>Writes a one-page PDF on first use and returns its path.</summary>
public sealed class GeneratedTestPageProvider(string path) : ITestPageProvider
{
    public string GetTestPagePath()
    {
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var builder = new PdfDocumentBuilder();
            var font = builder.AddStandard14Font(Standard14Font.Helvetica);
            builder.AddPage(PageSize.A4).AddText("Mailflow - page de test d'impression", 16, new PdfPoint(50, 780), font);
            File.WriteAllBytes(path, builder.Build());
        }
        return path;
    }
}
