using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Xps.Packaging;
using Broadside.Rendering;
using Broadside.TestSupport;
using Broadside.Viewer.Wpf.Tests;

namespace Broadside.Reader.Tests;

public sealed class PdfPrintPaginatorTests
{
    [Fact]
    public void Print_is_disabled_without_an_open_document() => Sta.Run(() =>
    {
        using var window = new MainWindow(null);
        Assert.False(ApplicationCommands.Print.CanExecute(null, window));
        return Task.CompletedTask;
    });

    [Fact]
    public void Printing_fits_the_selected_pages_and_writes_normal_colors_to_xps() => Sta.Run(async () =>
    {
        using var directory = new TestDirectory();
        string source = directory.PathOf("pages.pdf");
        await File.WriteAllBytesAsync(source, ManyPagesDocument.Create(3), TestContext.Current.CancellationToken);
        await using DocumentFile file = await DocumentFile.OpenAsync(source);
        using (file.Session.BeginEdit())
        {
            file.Session.Document.Pages[1].Rotation = 90;
        }

        file.Session.ColorFilter = ColorFilter.DarkMode;
        var size = new Size(600, 800);
        var area = new Rect(24, 30, 552, 740);
        var paginator = new PdfPrintPaginator(file.Session, size, area, 1, 2);
        using DocumentPage page = paginator.GetPage(0);
        var visual = Assert.IsType<DrawingVisual>(page.Visual);
        var drawing = Assert.IsType<ImageDrawing>(visual.Drawing.Children[1]);
        Assert.True(drawing.Rect.Width > drawing.Rect.Height);
        Assert.True(area.Contains(drawing.Rect));
        var image = Assert.IsAssignableFrom<BitmapSource>(drawing.ImageSource);
        byte[] corner = new byte[4];
        image.CopyPixels(new Int32Rect(0, 0, 1, 1), corner, 4, 0);
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, corner);
        byte[] pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        Assert.Contains(pixels, channel => channel < 200);
        Assert.Same(DocumentPage.Missing, paginator.GetPage(2));

        string output = directory.PathOf("printed.xps");
        using (var xps = new XpsDocument(output, FileAccess.ReadWrite))
        {
            XpsDocument.CreateXpsDocumentWriter(xps).Write(paginator);
        }

        using var printed = new XpsDocument(output, FileAccess.Read);
        DocumentPaginator result = printed.GetFixedDocumentSequence().DocumentPaginator;
        result.ComputePageCount();
        Assert.Equal(2, result.PageCount);
    });
}
