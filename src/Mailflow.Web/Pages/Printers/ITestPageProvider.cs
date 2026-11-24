namespace Mailflow.Web.Pages.Printers;

/// <summary>Supplies the path of the PDF sent by "Test d'impression".</summary>
public interface ITestPageProvider
{
    string GetTestPagePath();
}
