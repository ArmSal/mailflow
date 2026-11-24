using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Mailflow.Web.Pages;

public static class PartialExtensions
{
    /// <summary>
    /// Returns a partial whose own view data carries <paramref name="data"/>. Values set on the page model's
    /// ViewData before a plain <c>Partial(...)</c> call do not reach the rendered partial.
    /// </summary>
    public static PartialViewResult PartialWith(this PageModel page, string viewName, object? model, params (string Key, object? Value)[] data)
    {
        var viewData = new ViewDataDictionary(page.MetadataProvider, page.ModelState) { Model = model };
        foreach (var (key, value) in data) viewData[key] = value;
        return new PartialViewResult { ViewName = viewName, ViewData = viewData };
    }
}
