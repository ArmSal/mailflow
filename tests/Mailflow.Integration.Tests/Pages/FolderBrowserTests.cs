using Mailflow.Web.Pages.Settings;

namespace Mailflow.Integration.Tests.Pages;

public class FolderBrowserTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("mf-browse").FullName;

    public FolderBrowserTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "b"));
        Directory.CreateDirectory(Path.Combine(_root, "a"));
    }

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void Lists_sub_folders_sorted_and_offers_the_parent()
    {
        var l = FolderBrowser.List(_root);
        Assert.Equal(new[] { "a", "b" }, l.Folders.Select(Path.GetFileName));
        Assert.Equal(Directory.GetParent(_root)!.FullName, l.Parent);
        Assert.Null(l.Error);
    }

    [Fact]
    public void Missing_folder_falls_back_to_the_nearest_existing_parent() =>
        Assert.Equal(Path.Combine(_root, "a"), FolderBrowser.List(Path.Combine(_root, "a", "nope", "deeper")).Path);

    [Fact]
    public void Empty_path_lists_the_drives()
    {
        var l = FolderBrowser.List("");
        Assert.Equal("", l.Path);
        Assert.Null(l.Parent);
        Assert.NotEmpty(l.Folders);
    }
}
