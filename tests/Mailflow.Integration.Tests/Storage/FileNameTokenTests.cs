using Mailflow.Infrastructure.Storage;

namespace Mailflow.Integration.Tests.Storage;

public class FileNameTokenTests
{
    [Fact]
    public void Day_tokens_are_expanded()
    {
        var path = FileSystemDocumentStore.BuildRelativePath("{Category}/{dd}/{dd-MM-yyyy}_{OriginalName}.pdf", "Cat", "S", "x.pdf", new DateTime(2026, 3, 7));
        Assert.Equal(Path.Combine("Cat", "07", "07-03-2026_x.pdf"), path);
    }
}
