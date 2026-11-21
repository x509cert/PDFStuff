using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Broadside.Rendering;
using Broadside.Viewer;

namespace Broadside.Reader;

internal sealed class PdfPrintPaginator : DocumentPaginator
{
    private const double Dpi = 300;
    private const double MaxPixels = 4096;
    private readonly DocumentSession _session;
    private readonly Rect _area;
    private readonly int _first;

    public PdfPrintPaginator(DocumentSession session, Size size, Rect area, int first, int last)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (first < 0 || last < first || last >= session.PageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(first));
        }

        _session = session;
        _area = area;
        _first = first;
        PageSize = size;
        PageCount = last - first + 1;
    }

    public override bool IsPageCountValid => true;

    public override int PageCount { get; }

    public override Size PageSize { get; set; }

    public override IDocumentPaginatorSource Source => null!;

    public override DocumentPage GetPage(int pageNumber)
    {
        if (pageNumber < 0 || pageNumber >= PageCount)
        {
            return DocumentPage.Missing;
        }

        (BitmapSource image, Size size) = Task.Run(async () =>
        {
            int index = _first + pageNumber;
            PageMetrics metrics = await _session.GetPageMetricsAsync(index).ConfigureAwait(false);
            double fit = Math.Min(_area.Width / metrics.Width, _area.Height / metrics.Height);
            double scale = Math.Min(fit * Dpi / 96, MaxPixels / Math.Max(metrics.Width, metrics.Height));
            using var renderer = new SoftwareRasterizer();
            using Bitmap bitmap = renderer.Render(_session.Document.Pages[index],
                new Broadside.Rendering.RenderOptions().WithScale(scale).WithIntent(DisplayIntent.Print));
            BitmapSource source = BitmapSource.Create(bitmap.Width, bitmap.Height, Dpi, Dpi,
                PixelFormats.Pbgra32, null, bitmap.Pixels.ToArray(), bitmap.Stride);
            source.Freeze();
            return (source, new Size(metrics.Width * fit, metrics.Height * fit));
        }).GetAwaiter().GetResult();

        var visual = new DrawingVisual();
        using (DrawingContext drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(PageSize));
            drawing.DrawImage(image, new Rect(_area.X + (_area.Width - size.Width) / 2,
                _area.Y + (_area.Height - size.Height) / 2, size.Width, size.Height));
        }

        return new DocumentPage(visual, PageSize, new Rect(PageSize), _area);
    }
}
