using Mailflow.Application.Abstractions;
using UglyToad.PdfPig;

namespace Mailflow.Infrastructure.Pdf;

public sealed class PdfPigTextReader : IPdfTextReader
{
    public string ReadText(byte[] pdf, int maxPages = 2)
    {
        try
        {
            if (pdf is null || pdf.Length == 0 || maxPages <= 0) return "";
            using var doc = PdfDocument.Open(pdf);
            var pages = Math.Min(maxPages, doc.NumberOfPages);
            var parts = new List<string>(pages);
            for (var i = 1; i <= pages; i++) parts.Add(doc.GetPage(i).Text);
            return string.Join("\n", parts);
        }
        catch
        {
            return "";
        }
    }
}
