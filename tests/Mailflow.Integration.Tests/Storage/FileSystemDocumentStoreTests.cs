using Mailflow.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace Mailflow.Integration.Tests.Storage;

public sealed class FileSystemDocumentStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mailflow-tests-" + Guid.NewGuid().ToString("N"), "docs");
    private readonly FileSystemDocumentStore _store;
    private static readonly DateTime Date = new(2026, 3, 7);

    public FileSystemDocumentStoreTests()
        => _store = new(Options.Create(new DocumentStoreOptions { DocumentsRoot = _root }));

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_root)!, true);

    private static byte[] Bytes(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    private bool Inside(string path) => Path.GetFullPath(path).StartsWith(Path.GetFullPath(_root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public async Task Saves_with_pattern_and_creates_folders()
    {
        var r = await _store.SaveAsync(Bytes("a"), "Factures", "ACME", "inv.pdf", Date, default);
        Assert.False(r.WasDuplicate);
        Assert.Equal(Path.Combine(Path.GetFullPath(_root), "Factures", "2026", "03", "2026-03-07_ACME_inv.pdf"), r.Path);
        Assert.Equal(Bytes("a"), await File.ReadAllBytesAsync(r.Path));
        Assert.Equal(64, r.Sha256.Length);
    }

    [Fact]
    public async Task Same_content_is_duplicate_and_returns_existing_path()
    {
        var first = await _store.SaveAsync(Bytes("a"), "Factures", "ACME", "inv.pdf", Date, default);
        var second = await _store.SaveAsync(Bytes("a"), "Autre", "Other", "other.pdf", Date, default);
        Assert.True(second.WasDuplicate);
        Assert.Equal(first.Path, second.Path);
        Assert.Equal(first.Sha256, second.Sha256);
    }

    [Fact]
    public async Task Different_content_same_name_gets_unique_name()
    {
        var a = await _store.SaveAsync(Bytes("a"), "C", "S", "x.pdf", Date, default);
        var b = await _store.SaveAsync(Bytes("b"), "C", "S", "x.pdf", Date, default);
        Assert.False(b.WasDuplicate);
        Assert.NotEqual(a.Path, b.Path);
        Assert.Equal(Bytes("a"), await File.ReadAllBytesAsync(a.Path));
        Assert.Equal(Bytes("b"), await File.ReadAllBytesAsync(b.Path));
    }

    [Theory]
    [InlineData("..\\..\\evil.pdf", "S", "C")]
    [InlineData("../../evil.pdf", "S", "C")]
    [InlineData("C:\\Windows\\evil.pdf", "S", "C")]
    [InlineData("/etc/passwd", "S", "C")]
    [InlineData("x.pdf", "..\\..\\evil", "C")]
    [InlineData("x.pdf", "S", "..\\..\\evil")]
    [InlineData("x.pdf", "S", "C:\\Windows")]
    [InlineData("x.pdf", "S", "\\\\server\\share")]
    [InlineData("x.pdf", "S", "..")]
    [InlineData("x.pdf", "..", "C")]
    public async Task Path_traversal_stays_inside_root(string name, string supplier, string category)
    {
        var r = await _store.SaveAsync(Bytes(name + supplier + category), category, supplier, name, Date, default);
        Assert.True(Inside(r.Path), r.Path);
        Assert.True(File.Exists(r.Path));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("nul.pdf")]
    [InlineData("COM1")]
    [InlineData("lpt9.txt")]
    public async Task Reserved_windows_names_are_neutralised(string name)
    {
        var r = await _store.SaveAsync(Bytes(name), name, name, name, Date, default);
        Assert.True(Inside(r.Path));
        foreach (var seg in Path.GetRelativePath(_root, r.Path).Split(Path.DirectorySeparatorChar))
            Assert.DoesNotContain(seg.Split('.')[0].ToUpperInvariant(), new[] { "CON", "NUL", "COM1", "LPT9" });
    }

    [Fact]
    public async Task Invalid_characters_are_stripped()
    {
        var r = await _store.SaveAsync(Bytes("z"), "C", "Ac<m>e|?*", "a\"b:c.pdf", Date, default);
        var file = Path.GetFileName(r.Path);
        Assert.Equal(-1, file.IndexOfAny(Path.GetInvalidFileNameChars()));
        Assert.True(Inside(r.Path));
    }

    [Fact]
    public async Task Move_changes_category_subfolder()
    {
        var r = await _store.SaveAsync(Bytes("m"), "A trier", "ACME", "inv.pdf", Date, default);
        await _store.MoveAsync(r.Path, "Factures", default);
        Assert.False(File.Exists(r.Path));
        Assert.True(File.Exists(Path.Combine(Path.GetFullPath(_root), "Factures", "2026", "03", "2026-03-07_ACME_inv.pdf")));
    }

    [Fact]
    public async Task Move_rejects_paths_outside_root_and_traversal_in_target()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.MoveAsync(Path.Combine(Path.GetTempPath(), "x.pdf"), "C", default));
        var r = await _store.SaveAsync(Bytes("m"), "A", "S", "i.pdf", Date, default);
        await _store.MoveAsync(r.Path, "..\\..\\evil", default);
        Assert.True(Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).All(Inside));
        Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(_root)!, "*", SearchOption.AllDirectories));
    }
}
