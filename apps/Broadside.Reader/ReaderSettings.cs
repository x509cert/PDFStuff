using System.IO;
using System.Text.Json;
using Broadside.Viewer;

namespace Broadside.Reader;

internal sealed class ReaderSettings
{
    public double Width { get; set; } = 1100;

    public double Height { get; set; } = 800;

    public bool Maximized { get; set; }

    public bool DarkMode { get; set; }

    public bool SidebarVisible { get; set; } = true;

    public PageLayoutMode Layout { get; set; } = PageLayoutMode.Continuous;

    public List<string> RecentFiles { get; set; } = [];

    public static string DefaultPath => Environment.GetEnvironmentVariable("BROADSIDE_READER_SETTINGS") is { Length: > 0 } path
        ? path
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Broadside", "Reader", "settings.json");

    public static ReaderSettings Read(string path)
    {
        try
        {
            ReaderSettings settings = JsonSerializer.Deserialize<ReaderSettings>(File.ReadAllText(path)) ?? new ReaderSettings();
            settings.Width = double.IsFinite(settings.Width) ? Math.Clamp(settings.Width, 640, 3840) : 1100;
            settings.Height = double.IsFinite(settings.Height) ? Math.Clamp(settings.Height, 480, 2160) : 800;
            if (!Enum.IsDefined(settings.Layout))
            {
                settings.Layout = PageLayoutMode.Continuous;
            }

            settings.RecentFiles = settings.RecentFiles?
                .Where(file => !string.IsNullOrWhiteSpace(file))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList() ?? [];
            return settings;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new ReaderSettings();
        }
    }

    public void AddRecent(string path)
    {
        RecentFiles.RemoveAll(file => string.Equals(file, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > 10)
        {
            RecentFiles.RemoveRange(10, RecentFiles.Count - 10);
        }
    }

    public void Save(string path)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this));
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
