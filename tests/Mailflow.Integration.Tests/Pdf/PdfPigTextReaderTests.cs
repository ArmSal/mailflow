using Mailflow.Infrastructure.Pdf;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Fonts.Standard14Fonts;

namespace Mailflow.Integration.Tests.Pdf;

public static class PdfFixture
{
    public static byte[] Make(params string[] pageTexts)
    {
        var b = new PdfDocumentBuilder();
        var font = b.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var text in pageTexts)
            b.AddPage(PageSize.A4).AddText(text, 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        return b.Build();
    }
}

public class PdfPigTextReaderTests
{
    private readonly PdfPigTextReader _reader = new();

    [Fact]
    public void Reads_first_pages_only()
    {
        var pdf = PdfFixture.Make("PAGEONE", "PAGETWO", "PAGETHREE");
        var text = _reader.ReadText(pdf, 2);
        Assert.Contains("PAGEONE", text);
        Assert.Contains("PAGETWO", text);
        Assert.DoesNotContain("PAGETHREE", text);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 1, 2, 3, 4 })]
    public void Corrupt_input_returns_empty(byte[] data) => Assert.Equal("", _reader.ReadText(data));

    [Fact]
    public void Truncated_pdf_returns_empty_without_throwing()
    {
        var pdf = PdfFixture.Make("HELLO");
        Assert.Equal("", _reader.ReadText(pdf[..(pdf.Length / 3)]));
    }

    [Fact]
    public void Email_renderer_creates_readable_pdf_and_handles_unicode_emojis()
    {
        var renderer = new PdfPigEmailRenderer();
        var msg = new Mailflow.Application.Abstractions.MailMessage(
            "id-1", 1, new DateTime(2026, 11, 24, 21, 0, 0, DateTimeKind.Utc),
            "client@exemple.fr", "boutique@exemple.fr",
            "PPPrint commande reçu 😀",
            "Bonjour,\nVoici le détail : 42,50 € — merci d’imprimer !\n\nCordialement.",
            []);

        var bytes = renderer.Render(msg);
        var text = _reader.ReadText(bytes, 5);

        Assert.Contains("PPPrint", text);
        Assert.Contains("client@exemple.fr", text);
        Assert.Contains("42,50 EUR", text);
    }
}
