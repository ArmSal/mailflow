using Mailflow.Application.Processing;
using Mailflow.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using CategoryEdit = Mailflow.Web.Pages.Categories.EditModel;
using CategoryIndex = Mailflow.Web.Pages.Categories.IndexModel;

namespace Mailflow.Integration.Tests.Pages;

public sealed class DefaultCategoryTests : IDisposable
{
    private readonly SqliteFixture _fx = new();
    public void Dispose() => _fx.Dispose();

    private int AddDefault()
    {
        using var db = _fx.NewDb();
        var c = new Category { Name = MessagePipeline.DefaultCategoryName, LocalSubfolder = "A trier" };
        db.Categories.Add(c);
        db.SaveChanges();
        return c.Id;
    }

    [Fact]
    public async Task Default_category_cannot_be_deleted()
    {
        var id = AddDefault();
        using var db = _fx.NewDb();
        await new CategoryIndex(db).With().OnPostDeleteAsync(id);
        Assert.Single(db.Categories);
    }

    [Fact]
    public async Task Default_category_cannot_be_renamed()
    {
        var id = AddDefault();
        using var db = _fx.NewDb();
        var page = new CategoryEdit(db).With();
        page.Input = new CategoryEdit.CategoryInput { Name = "Autre", LocalSubfolder = "A trier", Color = "#112233" };
        Assert.IsType<PageResult>(await page.OnPostAsync(id));
        Assert.Equal(MessagePipeline.DefaultCategoryName, db.Categories.Single().Name);
    }

    [Fact]
    public async Task Default_category_can_change_other_fields()
    {
        var id = AddDefault();
        using var db = _fx.NewDb();
        var page = new CategoryEdit(db).With();
        page.Input = new CategoryEdit.CategoryInput { Name = MessagePipeline.DefaultCategoryName, LocalSubfolder = "A trier", Color = "#112233" };
        Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(id));
        Assert.Equal("#112233", db.Categories.Single().Color);
    }
}
