namespace Mailflow.Web.Pages.Settings;

/// <param name="Path">Current folder; empty means "list the drives".</param>
/// <param name="Parent">Folder to go up to; empty means the drive list; null when already at the drive list.</param>
public sealed record FolderListing(string Path, string? Parent, IReadOnlyList<string> Folders, string? Error);

/// <summary>Lists folders on the PC running Mailflow so the settings page can offer a "Parcourir" button.</summary>
public static class FolderBrowser
{
    public static FolderListing List(string? path)
    {
        path = (path ?? "").Trim();
        while (path.Length > 0 && !Directory.Exists(path))
            path = Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? "";
        if (path.Length == 0)
        {
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.RootDirectory.FullName).ToList();
            return new FolderListing("", null, drives, null);
        }

        var full = Path.GetFullPath(path);
        var parent = Directory.GetParent(full)?.FullName ?? "";
        try
        {
            var folders = new DirectoryInfo(full).EnumerateDirectories()
                .Where(d => (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                .Select(d => d.FullName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            return new FolderListing(full, parent, folders, null);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return new FolderListing(full, parent, [], "Ce dossier n'est pas accessible.");
        }
    }
}
