using System.Diagnostics;
using System.Globalization;
using System.Windows.Automation;
using Broadside.TestSupport;

namespace Broadside.Reader.Tests;

[Collection("Reader application")]
[Trait("Category", "UIAutomation")]
public sealed class ApplicationTests
{
    [Fact]
    public async Task The_published_app_opens_zooms_searches_and_closes_outside_a_checkout()
    {
        using var directory = new TestDirectory();
        using Process app = Start(directory, Corpus.Path("tagged-structure.pdf"));
        try
        {
            AutomationElement window = await WaitAsync(() => MainWindow(app));
            await WaitTextAsync(window, "PageStatus", value => value == "Page 1 of 1");
            string before = Text(window, "ZoomStatus");
            Invoke(window, "ZoomIn");
            await WaitTextAsync(window, "ZoomStatus", value => value != before);

            var query = (ValuePattern)Find(window, "SearchBox").GetCurrentPattern(ValuePattern.Pattern);
            query.SetValue("Broadside");
            Invoke(window, "Find");
            await WaitTextAsync(window, "SearchStatus", value => value == "1 of 1");
            Assert.Equal(ControlType.Document, Find(window, "Viewer").Current.ControlType);
            ((WindowPattern)window.GetCurrentPattern(WindowPattern.Pattern)).Close();
            await app.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            Assert.Equal(0, app.ExitCode);
            Assert.True(File.Exists(directory.PathOf("settings.json")));
        }
        finally
        {
            if (!app.HasExited)
            {
                app.Kill(entireProcessTree: true);
                await app.WaitForExitAsync(TestContext.Current.CancellationToken);
            }
        }
    }

    [Fact]
    public async Task Launching_without_a_file_shows_the_welcome_screen()
    {
        using var directory = new TestDirectory();
        using Process app = Start(directory);
        try
        {
            AutomationElement window = await WaitAsync(() => MainWindow(app));
            Assert.False(Find(window, "WelcomeOpen").Current.IsOffscreen);
            Assert.False(Find(window, "SaveFile").Current.IsEnabled);
            ((WindowPattern)window.GetCurrentPattern(WindowPattern.Pattern)).Close();
            await app.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            Assert.Equal(0, app.ExitCode);
        }
        finally
        {
            if (!app.HasExited)
            {
                app.Kill(entireProcessTree: true);
                await app.WaitForExitAsync(TestContext.Current.CancellationToken);
            }
        }
    }

    private static Process Start(TestDirectory directory, string? path = null)
    {
        string executable = Environment.GetEnvironmentVariable("BROADSIDE_READER_EXE")
            ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "Broadside.Reader", "release", "Broadside.Reader.exe"));
        Assert.True(File.Exists(executable), "Build the app first, or set BROADSIDE_READER_EXE to the published executable.");
        var start = new ProcessStartInfo(executable) { WorkingDirectory = directory.PathOf(), UseShellExecute = false };
        start.Environment["BROADSIDE_READER_SETTINGS"] = directory.PathOf("settings.json");
        if (Environment.GetEnvironmentVariable("BROADSIDE_READER_EXE") is not null)
        {
            // A configured portable build must boot from its bundled runtime, not the SDK used to run these tests.
            start.Environment["DOTNET_ROOT"] = directory.PathOf("no-runtime");
            start.Environment["DOTNET_ROOT_X64"] = directory.PathOf("no-runtime");
        }

        if (path is not null)
        {
            start.ArgumentList.Add(path);
        }

        return Process.Start(start) ?? throw new InvalidOperationException("The reader did not start.");
    }

    private static AutomationElement? MainWindow(Process app)
    {
        Assert.False(app.HasExited, app.HasExited ? $"Reader exited with code {app.ExitCode}." : "");
        return AutomationElement.RootElement.FindFirst(TreeScope.Children, new AndCondition(
            new PropertyCondition(AutomationElement.ProcessIdProperty, app.Id),
            new PropertyCondition(AutomationElement.AutomationIdProperty, "ReaderWindow")));
    }

    private static AutomationElement Find(AutomationElement root, string id) =>
        root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id))
        ?? throw new InvalidOperationException("Missing control: " + id);

    private static string Text(AutomationElement root, string id) => Find(root, id).Current.Name;

    private static void Invoke(AutomationElement root, string id) =>
        ((InvokePattern)Find(root, id).GetCurrentPattern(InvokePattern.Pattern)).Invoke();

    private static async Task<AutomationElement> WaitAsync(Func<AutomationElement?> find)
    {
        long start = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(start) < TimeSpan.FromSeconds(60))
        {
            if (find() is { } element)
            {
                return element;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("The reader window did not appear.");
    }

    private static async Task WaitTextAsync(AutomationElement window, string id, Func<string, bool> expected)
    {
        long start = Stopwatch.GetTimestamp();
        string last = string.Empty;
        while (Stopwatch.GetElapsedTime(start) < TimeSpan.FromSeconds(60))
        {
            last = Text(window, id);
            if (expected(last))
            {
                return;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException(string.Create(CultureInfo.InvariantCulture, $"{id} did not reach the expected state; last value: {last}"));
    }
}

[CollectionDefinition("Reader application", DisableParallelization = true)]
public sealed class ReaderApplicationCollection;
