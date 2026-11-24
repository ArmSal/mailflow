using System.Text.Json;

namespace Mailflow.Desktop;

public sealed record WindowState(int X, int Y, int Width, int Height, bool Maximized)
{
    public static WindowState? Load(string path)
    {
        try { return JsonSerializer.Deserialize<WindowState>(File.ReadAllText(path)); }
        catch (Exception) { return null; }
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this));
        }
        catch (Exception) { /* not fatal */ }
    }
}
