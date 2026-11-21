using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Broadside.Rendering;
using Broadside.Viewer;
using Broadside.Viewer.Wpf;

namespace Broadside.Reader;

internal sealed class NavigationPanel(PdfView viewer, TreeView outline, ListBox pages, Action<string> report) : IDisposable
{
    private readonly ListBox _pages = pages;
    private DocumentSession? _session;
    private CancellationTokenSource? _loading;
    private CancellationTokenSource? _rendering;
    private PageItem[] _items = [];
    private int _page = -1;
    private bool _visible;
    private int _generation;
    private readonly HashSet<int> _pictures = [];

    public void SetSession(DocumentSession? session)
    {
        Cancel(ref _loading);
        Cancel(ref _rendering);
        _session = session;
        _generation++;
        _page = -1;
        _pictures.Clear();
        _items = [];
        outline.ItemsSource = null;
        _pages.ItemsSource = null;
        if (session is not null)
        {
            _loading = new CancellationTokenSource();
            _ = LoadAsync(session, _generation, _loading.Token);
        }
    }

    public void ShowPage(int page, bool visible)
    {
        if (_page == page && _visible == visible)
        {
            return;
        }

        _page = page;
        _visible = visible;
        if (page >= 0 && page < _items.Length)
        {
            _pages.SelectedIndex = page;
            _pages.ScrollIntoView(_items[page]);
        }

        RefreshThumbnails();
    }

    public void RefreshThumbnails()
    {
        Cancel(ref _rendering);
        if (_session is not { } session || !_visible || _items.Length == 0)
        {
            return;
        }

        int start = Math.Max(0, _page - 3);
        int end = Math.Min(_items.Length, _page + 4);
        foreach (int old in _pictures.ToArray())
        {
            _items[old].Image = null;
        }

        _pictures.Clear();
        _rendering = new CancellationTokenSource();
        CancellationToken token = _rendering.Token;
        int generation = _generation;
        int[] nearby = Enumerable.Range(start, end - start).OrderBy(index => Math.Abs(index - _page)).ToArray();
        _ = RenderAsync(session, generation, nearby, token);
    }

    public void Dispose()
    {
        SetSession(null);
    }

    private async Task LoadAsync(DocumentSession session, int generation, CancellationToken token)
    {
        try
        {
            DocumentOutline model = await DocumentOutline.LoadAsync(session, token);
            PageLabelTable labels = await PageLabelTable.LoadAsync(session, token);
            if (generation != _generation || token.IsCancellationRequested)
            {
                return;
            }

            outline.ItemsSource = model.Roots;
            _items = [.. Enumerable.Range(0, session.PageCount).Select(index => new PageItem(labels.GetLabel(index)))];
            _pages.ItemsSource = _items;
            _page = -1;
            ShowPage(viewer.State.PageIndex, _visible);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException) when (generation != _generation)
        {
        }
#pragma warning disable CA1031 // Navigation failures are reported on the application's status line.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            if (generation == _generation)
            {
                report("Navigation: " + exception.Message);
            }
        }
    }

    private async Task RenderAsync(DocumentSession session, int generation, int[] nearby, CancellationToken token)
    {
        try
        {
            var thumbnails = new PageThumbnails(session, new ThumbnailSurface(this, generation, token));
            thumbnails.Failed += (_, e) => _pages.Dispatcher.InvokeAsync(() =>
            {
                if (generation == _generation)
                {
                    report("Thumbnail: " + e.Exception.Message);
                }
            });
            await thumbnails.RenderAsync(nearby, token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException) when (generation != _generation || token.IsCancellationRequested)
        {
        }
#pragma warning disable CA1031 // Thumbnail failures must not end the UI dispatcher.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            if (generation == _generation)
            {
                report("Thumbnails: " + exception.Message);
            }
        }
    }

    private static void Cancel(ref CancellationTokenSource? cancellation)
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
    }

    internal sealed class PageItem(string label) : INotifyPropertyChanged
    {
        private ImageSource? _image;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Label { get; } = label;

        public ImageSource? Image
        {
            get => _image;
            set
            {
                _image = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image)));
            }
        }
    }

    private sealed class ThumbnailSurface(NavigationPanel panel, int generation, CancellationToken token) : IRenderSurface
    {
        public void Present(Tile tile, Bitmap pixels)
        {
            BitmapSource image = BitmapSource.Create(pixels.Width, pixels.Height, 96, 96, PixelFormats.Pbgra32, null, pixels.Pixels.ToArray(), pixels.Stride);
            image.Freeze();
            panel._pages.Dispatcher.InvokeAsync(() =>
            {
                if (generation == panel._generation && !token.IsCancellationRequested && tile.PageIndex < panel._items.Length)
                {
                    panel._items[tile.PageIndex].Image = image;
                    panel._pictures.Add(tile.PageIndex);
                }
            });
        }
    }
}
