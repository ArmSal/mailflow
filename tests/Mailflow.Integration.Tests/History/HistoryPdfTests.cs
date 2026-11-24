using Mailflow.Application.Commands;
using Mailflow.Application.Queries;
using Mailflow.Domain.Entities;
using Mailflow.Infrastructure.Storage;
using Mailflow.Integration.Tests.Pages;
using Mailflow.Web.Pages.History;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Mailflow.Integration.Tests.History;

public sealed class HistoryPdfTests : IDisposable
{
    private readonly SqliteFixture _fx = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mf-hist-" + Guid.NewGuid().ToString("N"));
    private readonly string _outside = Path.Combine(Path.GetTempPath(), "mf-hist-out-" + Guid.NewGuid().ToString("N"));

    public HistoryPdfTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_outside);
    }

    public void Dispose()
    {
        _fx.Dispose();
        Directory.Delete(_root, true);
        Directory.Delete(_outside, true);
    }

    private int AddDoc(string storedPath)
    {
        using var db = _fx.NewDb();
        db.Documents.Add(new Document { ProcessedMessageId = 1, Sha256 = "x", OriginalName = "f.pdf", StoredPath = storedPath, CategoryId = 1 });
        db.SaveChanges();
        return db.Documents.OrderByDescending(d => d.Id).First().Id;
    }

    private IndexModel Page(TestDb db)
    {
        var page = new IndexModel(db, new DocumentSearch(db), null!, null!, Options.Create(new DocumentStoreOptions { DocumentsRoot = _root })).With();
        page.PageContext.HttpContext = new DefaultHttpContext();
        return page;
    }

    [Fact]
    public async Task Serves_inline_pdf_with_nosniff()
    {
        var file = Path.Combine(_root, "Factures", "a.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllBytesAsync(file, "%PDF-1.4"u8.ToArray());
        var id = AddDoc(file);
        using var db = _fx.NewDb();
        var page = Page(db);

        var result = await page.OnGetPdfAsync(id, default);

        var pf = Assert.IsType<PhysicalFileResult>(result);
        Assert.Equal("application/pdf", pf.ContentType);
        Assert.Equal("nosniff", page.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.Equal("inline", page.Response.Headers["Content-Disposition"].ToString());
    }

    [Fact]
    public async Task Path_outside_root_is_404_even_if_file_exists()
    {
        var file = Path.Combine(_outside, "secret.pdf");
        await File.WriteAllBytesAsync(file, "%PDF-1.4"u8.ToArray());
        var id = AddDoc(file);
        using var db = _fx.NewDb();
        Assert.IsType<NotFoundResult>(await Page(db).OnGetPdfAsync(id, default));
    }

    [Fact]
    public async Task Traversal_in_stored_path_is_404()
    {
        var file = Path.Combine(_outside, "secret.pdf");
        await File.WriteAllBytesAsync(file, "%PDF-1.4"u8.ToArray());
        var id = AddDoc(Path.Combine(_root, "..", Path.GetFileName(_outside), "secret.pdf"));
        using var db = _fx.NewDb();
        Assert.IsType<NotFoundResult>(await Page(db).OnGetPdfAsync(id, default));
    }

    [Fact]
    public async Task Missing_file_is_404()
    {
        var id = AddDoc(Path.Combine(_root, "Factures", "gone.pdf"));
        using var db = _fx.NewDb();
        Assert.IsType<NotFoundResult>(await Page(db).OnGetPdfAsync(id, default));
    }

    [Fact]
    public async Task Unknown_document_is_404()
    {
        using var db = _fx.NewDb();
        Assert.IsType<NotFoundResult>(await Page(db).OnGetPdfAsync(999, default));
    }
}
