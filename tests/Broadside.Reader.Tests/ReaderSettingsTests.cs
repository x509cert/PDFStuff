using Broadside.Viewer;

namespace Broadside.Reader.Tests;

public sealed class ReaderSettingsTests
{
    [Fact]
    public void Missing_or_invalid_preferences_do_not_prevent_startup()
    {
        using var directory = new TestDirectory();
        string path = directory.PathOf("settings.json");
        Assert.Empty(ReaderSettings.Read(path).RecentFiles);
        File.WriteAllText(path, "{broken");
        Assert.Equal(PageLayoutMode.Continuous, ReaderSettings.Read(path).Layout);
        File.WriteAllText(path, """{"Width":-10,"Height":100000,"Layout":999,"RecentFiles":null}""");
        ReaderSettings settings = ReaderSettings.Read(path);
        Assert.Equal(640, settings.Width);
        Assert.Equal(2160, settings.Height);
        Assert.Equal(PageLayoutMode.Continuous, settings.Layout);
        Assert.Empty(settings.RecentFiles);
    }

    [Fact]
    public void Preferences_round_trip_and_recent_files_are_bounded_and_case_insensitive()
    {
        using var directory = new TestDirectory();
        string path = directory.PathOf("preferences", "settings.json");
        var settings = new ReaderSettings { DarkMode = true, SidebarVisible = false, Maximized = true, Layout = PageLayoutMode.TwoUp };
        for (int index = 0; index < 15; index++)
        {
            settings.AddRecent(directory.PathOf(index + ".pdf"));
        }

        string recent = settings.RecentFiles[5];
        settings.AddRecent(recent.ToUpperInvariant());
        settings.Save(path);
        ReaderSettings read = ReaderSettings.Read(path);
        Assert.True(read.DarkMode);
        Assert.False(read.SidebarVisible);
        Assert.True(read.Maximized);
        Assert.Equal(PageLayoutMode.TwoUp, read.Layout);
        Assert.Equal(10, read.RecentFiles.Count);
        Assert.Equal(recent.ToUpperInvariant(), read.RecentFiles[0]);
        Assert.Single(read.RecentFiles, file => string.Equals(file, recent, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }
}
