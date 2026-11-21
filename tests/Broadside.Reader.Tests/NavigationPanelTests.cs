using System.Diagnostics;
using System.Windows.Controls;
using Broadside.TestSupport;
using Broadside.Viewer;
using Broadside.Viewer.Wpf;
using Broadside.Viewer.Wpf.Tests;

namespace Broadside.Reader.Tests;

public sealed class NavigationPanelTests
{
    [Fact]
    public void Visible_thumbnails_survive_scrolling_and_return_when_scrolled_back() => Sta.Run(async () =>
    {
        await using DocumentSession session = DocumentSession.Open(ManyPagesDocument.Create(8));
        using var viewer = new PdfView();
        var pages = new ListBox();
        var errors = new List<string>();
        using var navigation = new NavigationPanel(viewer, new TreeView(), pages, errors.Add);
        navigation.SetSession(session);
        await WaitAsync(() => pages.Items.Count == 8, errors);

        navigation.ShowPage(7, true);
        navigation.ShowThumbnails(3, 5);
        await WaitAsync(() => Enumerable.Range(3, 5).All(index => Item(index).Image is not null), errors);
        var retained = Item(3).Image;
        navigation.ShowThumbnails(0, 3);
        Assert.Same(retained, Item(3).Image);
        await WaitAsync(() => Enumerable.Range(0, 3).All(index => Item(index).Image is not null), errors);
        Assert.All(Enumerable.Range(4, 4), index => Assert.Null(Item(index).Image));
        Assert.Empty(errors);

        NavigationPanel.PageItem Item(int index) => (NavigationPanel.PageItem)pages.Items[index];
    });

    private static async Task WaitAsync(Func<bool> ready, List<string> errors)
    {
        long start = Stopwatch.GetTimestamp();
        while (!ready() && errors.Count == 0 && Stopwatch.GetElapsedTime(start) < TimeSpan.FromSeconds(30))
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Empty(errors);
        Assert.True(ready(), "The visible thumbnails did not finish rendering.");
    }
}
