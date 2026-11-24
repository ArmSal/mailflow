using Mailflow.Web.Pages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Mailflow.Integration.Tests.Pages;

public class PartialExtensionsTests
{
    private sealed class AnyPage : PageModel;

    private static AnyPage NewPage() => new()
    {
        PageContext = new PageContext(new ActionContext(new DefaultHttpContext(), new RouteData(), new PageActionDescriptor())),
        MetadataProvider = new EmptyModelMetadataProvider(),
    };

    [Fact]
    public void PartialWith_puts_the_model_and_every_value_on_the_partial_result_itself()
    {
        var result = NewPage().PartialWith("_Row", 42, ("Error", "boom"), ("StoreName", "Store A"));

        Assert.Equal("_Row", result.ViewName);
        Assert.Equal(42, result.ViewData.Model);
        Assert.Equal("boom", result.ViewData["Error"]);
        Assert.Equal("Store A", result.ViewData["StoreName"]);
    }

    [Fact]
    public void PartialWith_accepts_null_model_and_no_values()
    {
        var result = NewPage().PartialWith("_Row", null);
        Assert.Null(result.ViewData.Model);
    }
}
