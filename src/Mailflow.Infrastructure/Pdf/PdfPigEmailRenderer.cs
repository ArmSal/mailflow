using System.Text;
using Mailflow.Application.Abstractions;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Mailflow.Infrastructure.Pdf;

public sealed class PdfPigEmailRenderer : IEmailPdfRenderer
{
    private const double MarginX = 50;
    private const double TopY = 790;
    private const double BottomY = 50;
    private const double LineHeight = 14;
    private const int MaxLineChars = 85;

    public byte[] Render(MailMessage message)
    {
        var builder = new PdfDocumentBuilder();
        var regular = builder.AddStandard14Font(Standard14Font.Helvetica);
        var bold = builder.AddStandard14Font(Standard14Font.HelveticaBold);

        var page = builder.AddPage(PageSize.A4);
        var y = TopY;

        void NextPageIfNeeded()
        {
            if (y >= BottomY) return;
            page = builder.AddPage(PageSize.A4);
            y = TopY;
        }

        void DrawLine(string text, PdfDocumentBuilder.AddedFont font, double size)
        {
            NextPageIfNeeded();
            if (text.Length > 0)
                page.AddText(text, size, new PdfPoint(MarginX, y), font);
            y -= LineHeight;
        }

        foreach (var line in Wrap($"Objet : {(string.IsNullOrWhiteSpace(message.Subject) ? "(sans objet)" : message.Subject)}", MaxLineChars))
            DrawLine(line, bold, 12);
        y -= 4;

        foreach (var line in Wrap($"De : {message.From}", MaxLineChars))
            DrawLine(line, regular, 10.5);
        if (!string.IsNullOrWhiteSpace(message.To))
            foreach (var line in Wrap($"A : {message.To}", MaxLineChars))
                DrawLine(line, regular, 10.5);
        DrawLine($"Date : {message.ReceivedAt.ToLocalTime():dd/MM/yyyy HH:mm}", regular, 10.5);
        y -= 10;

        var body = string.IsNullOrWhiteSpace(message.Body) ? "(Contenu vide)" : message.Body;
        foreach (var line in Wrap(body, MaxLineChars))
            DrawLine(line, regular, 10.5);

        return builder.Build();
    }

    internal static IEnumerable<string> Wrap(string text, int maxChars)
    {
        var normalized = SanitizeForWinAnsi(text);
        foreach (var paragraph in normalized.Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                yield return "";
                continue;
            }
            var start = 0;
            while (start < paragraph.Length)
            {
                var remaining = paragraph.Length - start;
                if (remaining <= maxChars)
                {
                    yield return paragraph[start..];
                    break;
                }
                var breakAt = paragraph.LastIndexOf(' ', start + maxChars, maxChars);
                if (breakAt <= start)
                {
                    yield return paragraph.Substring(start, maxChars);
                    start += maxChars;
                }
                else
                {
                    yield return paragraph[start..breakAt];
                    start = breakAt + 1;
                }
            }
        }
    }

    internal static string SanitizeForWinAnsi(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var replaced = s
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace("\t", "    ")
            .Replace('\u2019', '\'')
            .Replace('\u2018', '\'')
            .Replace('\u201C', '"')
            .Replace('\u201D', '"')
            .Replace('\u2013', '-')
            .Replace('\u2014', '-')
            .Replace("\u2026", "...")
            .Replace("\u20AC", "EUR")
            .Replace("\u0153", "oe")
            .Replace("\u0152", "OE")
            .Replace("\u00E6", "ae")
            .Replace("\u00C6", "AE")
            .Replace('\u00A0', ' ')
            .Normalize(NormalizationForm.FormD);

        // PdfPig Standard14 Helvetica only encodes ASCII printable glyphs reliably; strip diacritics so French text stays readable.
        var sb = new StringBuilder(replaced.Length);
        for (var i = 0; i < replaced.Length; i++)
        {
            var ch = replaced[i];
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsHighSurrogate(ch))
            {
                if (i + 1 < replaced.Length && char.IsLowSurrogate(replaced[i + 1])) i++;
                sb.Append('?');
                continue;
            }
            sb.Append(ch == '\n' || (ch >= 0x20 && ch <= 0x7E) ? ch : '?');
        }
        return sb.ToString();
    }
}
