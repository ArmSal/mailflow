namespace Mailflow.Web.Pages.Categories;

public static class CategoryPath
{
    /// <summary>True when <paramref name="path"/> is a relative subfolder without traversal.</summary>
    public static bool IsValid(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (path.Length > 200 || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
        if (path.Contains(':') || path.Contains('*') || path.Contains('?')) return false;
        if (Path.IsPathRooted(path) || path[0] is '/' or '\\') return false;
        return !path.Split('/', '\\').Any(seg => seg is ".." or "." || seg.Length == 0 || seg != seg.Trim() || seg.EndsWith('.'));
    }
}
