using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Broadside.Rendering;
using Broadside.Security;
using Broadside.Viewer;
using Broadside.Viewer.Wpf;
using Microsoft.Win32;

namespace Broadside.Reader;

internal sealed partial class MainWindow : Window, IDisposable
{
    private readonly ReaderSettings _settings = ReaderSettings.Read(ReaderSettings.DefaultPath);
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly NavigationPanel _navigation;
    private DocumentFile? _file;
    private bool _busy;
    private bool _closing;
    private bool _closePending;
    private bool _revealFirst;
    private bool _updatingScroll;

    public MainWindow(string? path)
    {
        InitializeComponent();
        Width = _settings.Width;
        Height = _settings.Height;
        WindowState = _settings.Maximized ? WindowState.Maximized : WindowState.Normal;
        DarkMenu.IsChecked = _settings.DarkMode;
        SidebarMenu.IsChecked = _settings.SidebarVisible;
        ApplyPreferences();
        _navigation = new NavigationPanel(Viewer, OutlineTree, PagesList, Report);
        PopulateRecent();
        foreach (PageLayoutMode mode in Enum.GetValues<PageLayoutMode>())
        {
            var item = new MenuItem { Header = mode.ToString(), IsCheckable = true, IsChecked = mode == _settings.Layout };
            item.Click += (_, _) =>
            {
                _settings.Layout = mode;
                foreach (MenuItem other in LayoutMenu.Items)
                {
                    other.IsChecked = ReferenceEquals(other, item);
                }

                ApplyPreferences();
            };
            LayoutMenu.Items.Add(item);
        }

        Bind(ApplicationCommands.Open, async () => await OpenDialogAsync(), false);
        Bind(ApplicationCommands.Save, async () => await SaveAsync(false), true);
        Bind(ApplicationCommands.SaveAs, async () => await SaveAsync(true), true);
        Bind(ApplicationCommands.Close, CloseDocumentAsync, true);
        Bind(ApplicationCommands.Copy, async () => await Viewer.CopySelectionAsync(), true);
        Bind(ApplicationCommands.SelectAll, () => { Viewer.Selection?.SelectAll(); return Task.CompletedTask; }, true);
        Bind(ApplicationCommands.Find, () => { SearchBox.Focus(); SearchBox.SelectAll(); return Task.CompletedTask; }, true);
        InputBindings.Add(new KeyBinding(ApplicationCommands.SaveAs, Key.S, ModifierKeys.Control | ModifierKeys.Shift));

        Viewer.StateChanged += (_, e) => ShowState(e.State);
        Viewer.Error += (_, e) => Report("Document: " + e.Exception.Message);
        Viewer.ActionRequested += OnActionRequested;
        _searchTimer.Tick += (_, _) =>
        {
            ShowSearch();
            UpdateDocumentScroll();
        };
        _searchTimer.Start();
        Closing += OnClosing;
        Loaded += async (_, _) =>
        {
            if (path is not null)
            {
                await RunAsync(() => OpenAsync(path));
            }
        };
    }

    private void Bind(RoutedCommand command, Func<Task> execute, bool needsDocument) =>
        CommandBindings.Add(new CommandBinding(command,
            async (_, _) => await RunAsync(execute),
            (_, e) => e.CanExecute = !_busy && (!_closing) && (!needsDocument || _file is not null)));

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy || _closing)
        {
            return;
        }

        SetBusy(true);
        try
        {
            await action();
        }
#pragma warning disable CA1031 // UI command failures are reported instead of escaping into the dispatcher.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            Report(exception.Message);
            MessageBox.Show(this, exception.Message, "SneakyPdf", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool value)
    {
        _busy = value;
        MainMenu.IsEnabled = !value;
        Toolbar.IsEnabled = !value;
        Viewer.IsEnabled = !value;
        Sidebar.IsEnabled = !value;
        AllowDrop = !value;
        UpdateDocumentScroll();
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task OpenDialogAsync()
    {
        var dialog = new OpenFileDialog { Filter = "PDF documents (*.pdf)|*.pdf|All files (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
        {
            await OpenAsync(dialog.FileName);
        }
    }

    private async Task OpenAsync(string path)
    {
        Report("Opening " + Path.GetFileName(path) + "...");
        DocumentFile? opened = null;
        try
        {
            PdfPassword? password = null;
            while (opened is null)
            {
                try
                {
                    opened = await DocumentFile.OpenAsync(path, password);
                }
                catch (PdfPasswordException exception)
                {
                    var prompt = new PasswordDialog(this, exception.Failure == PdfPasswordFailure.Incorrect);
                    if (prompt.ShowDialog() != true)
                    {
                        Report("Opening cancelled.");
                        return;
                    }

                    password = prompt.TakePassword();
                }
            }

            if (!await ConfirmDiscardAsync())
            {
                return;
            }

            await ReleaseDocumentAsync();
            _file = opened;
            opened = null;
            Viewer.Session = _file.Session;
            _navigation.SetSession(_file.Session);
            SearchBox.Clear();
            _settings.AddRecent(_file.Path);
            PopulateRecent();
            Welcome.Visibility = Visibility.Collapsed;
            UpdateTitle();
            Report("Opened " + Path.GetFileName(_file.Path));
            Viewer.Focus();
        }
        finally
        {
            if (opened is not null)
            {
                await opened.DisposeAsync();
            }
        }
    }

    private async Task<bool> SaveAsync(bool saveAs)
    {
        if (_file is not { } file)
        {
            return false;
        }

        Viewer.Forms?.Commit();
        string destination = file.Path;
        if (saveAs)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "PDF documents (*.pdf)|*.pdf",
                DefaultExt = ".pdf",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = Path.GetFileNameWithoutExtension(file.Path) + "-edited.pdf",
                InitialDirectory = Path.GetDirectoryName(file.Path),
            };
            if (dialog.ShowDialog(this) != true)
            {
                return false;
            }

            destination = dialog.FileName;
        }

        Report("Saving...");
        await file.SaveAsync(destination);
        _settings.AddRecent(file.Path);
        PopulateRecent();
        UpdateTitle();
        Report("Saved " + Path.GetFileName(file.Path));
        return true;
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        Viewer.Forms?.Commit();
        if (_file is not { IsDirty: true })
        {
            return true;
        }

        MessageBoxResult answer = MessageBox.Show(this, "Save changes before closing this document?", "Unsaved changes",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer == MessageBoxResult.No || (answer == MessageBoxResult.Yes && await SaveAsync(false));
    }

    private async Task CloseDocumentAsync()
    {
        if (await ConfirmDiscardAsync())
        {
            await ReleaseDocumentAsync();
        }
    }

    private async Task ReleaseDocumentAsync()
    {
        _navigation.SetSession(null);
        Viewer.Session = null;
        _observedAnnotations = null;
        DocumentFile? previous = _file;
        _file = null;
        if (previous is not null)
        {
            await previous.DisposeAsync();
        }

        Welcome.Visibility = Visibility.Visible;
        SearchBox.Clear();
        SearchStatus.Text = string.Empty;
        UpdateTitle();
        ShowState(Viewer.State);
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closing)
        {
            return;
        }

        e.Cancel = true;
        if (_busy || _closePending)
        {
            return;
        }

        _closePending = true;
        await RunAsync(async () =>
        {
            if (!await ConfirmDiscardAsync())
            {
                return;
            }

            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            _settings.Width = bounds.Width;
            _settings.Height = bounds.Height;
            _settings.Maximized = WindowState == WindowState.Maximized;
            try
            {
                _settings.Save(ReaderSettings.DefaultPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, "Preferences could not be saved: " + exception.Message, "SneakyPdf");
            }

            await ReleaseDocumentAsync();
            Dispose();
            _closing = true;
            _ = Dispatcher.InvokeAsync(Close);
        });
        _closePending = false;
    }

    private void ShowState(PdfViewState state)
    {
        PageStatus.Text = state.IsLoading ? "Opening..." : state.PageCount == 0 ? "No document" : $"Page {state.PageIndex + 1} of {state.PageCount}";
        PageCount.Text = state.PageCount == 0 ? string.Empty : "/ " + state.PageCount.ToString(CultureInfo.CurrentCulture);
        if (!PageNumber.IsKeyboardFocused)
        {
            PageNumber.Text = state.PageCount == 0 ? string.Empty : (state.PageIndex + 1).ToString(CultureInfo.CurrentCulture);
        }

        ZoomStatus.Text = state.PageCount == 0 ? string.Empty : state.Zoom.ToString("P0", CultureInfo.CurrentCulture);
        if (Viewer.Annotations is { } annotations && !ReferenceEquals(annotations, _observedAnnotations))
        {
            _observedAnnotations = annotations;
            annotations.Invalidated += (_, _) =>
            {
                if (ReferenceEquals(Viewer.Annotations, annotations))
                {
                    _file?.MarkChanged();
                    UpdateTitle();
                }
            };
            annotations.XfaFormDetected += (_, notice) => Report(notice.Explanation);
            if (annotations.XfaForm is { } xfa)
            {
                Report(xfa.Explanation);
            }
        }

        _navigation.ShowPage(state.PageIndex, Sidebar.SelectedIndex == 1 && _settings.SidebarVisible);
        UpdateDocumentScroll();
    }

    private IScrollProvider? ScrollProvider =>
        UIElementAutomationPeer.CreatePeerForElement(Viewer).GetPattern(PatternInterface.Scroll) as IScrollProvider;

    private void UpdateDocumentScroll()
    {
        _updatingScroll = true;
        try
        {
            IScrollProvider? provider = ScrollProvider;
            bool scrollable = provider?.VerticallyScrollable == true;
            DocumentScroll.IsEnabled = scrollable && !_busy;
            double view = scrollable ? Math.Clamp(provider!.VerticalViewSize, 0, 100) : 100;
            DocumentScroll.ViewportSize = view < 100 ? 100 * view / (100 - view) : 100;
            DocumentScroll.LargeChange = Math.Min(100, DocumentScroll.ViewportSize);
            DocumentScroll.SmallChange = Math.Min(100, DocumentScroll.ViewportSize / 10);
            DocumentScroll.Value = scrollable ? Math.Clamp(provider!.VerticalScrollPercent, 0, 100) : 0;
        }
        finally
        {
            _updatingScroll = false;
        }
    }

    private void OnDocumentScrollChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_updatingScroll && !_busy)
        {
            ScrollProvider?.SetScrollPercent(ScrollPatternIdentifiers.NoScroll, e.NewValue);
        }
    }

    private AnnotationLayer? _observedAnnotations;

    public void Dispose()
    {
        _searchTimer.Stop();
        _navigation.Dispose();
        Viewer.Dispose();
    }

    private void UpdateTitle() => Title = _file is null ? "SneakyPdf" : $"{Path.GetFileName(_file.Path)}{(_file.IsDirty ? " *" : "")} - SneakyPdf";

    private void Report(string message) => Message.Text = message;

    private void PopulateRecent()
    {
        RecentMenu.Items.Clear();
        RecentMenu.IsEnabled = _settings.RecentFiles.Count > 0;
        foreach (string path in _settings.RecentFiles)
        {
            var item = new MenuItem { Header = path.Replace("_", "__", StringComparison.Ordinal) };
            item.Click += async (_, _) => await RunAsync(() => OpenAsync(path));
            RecentMenu.Items.Add(item);
        }

        if (_settings.RecentFiles.Count > 0)
        {
            RecentMenu.Items.Add(new Separator());
            var clear = new MenuItem { Header = "Clear recent files" };
            clear.Click += (_, _) => { _settings.RecentFiles.Clear(); PopulateRecent(); };
            RecentMenu.Items.Add(clear);
        }
    }

    private void ApplyPreferences()
    {
        Viewer.ColorFilter = _settings.DarkMode ? ColorFilter.DarkMode : ColorFilter.None;
        if (Viewer.LayoutOptions?.Mode != _settings.Layout)
        {
            Viewer.LayoutOptions = new PageLayoutOptions().WithMode(_settings.Layout);
        }
        SidebarColumn.Width = new GridLength(_settings.SidebarVisible ? 220 : 0);
        Sidebar.Visibility = _settings.SidebarVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Find()
    {
        if (Viewer.Search is { } search)
        {
            _revealFirst = true;
            search.Search(SearchBox.Text, null, Viewer.State.PageIndex);
            ShowSearch();
        }
    }

    private void ShowSearch()
    {
        if (Viewer.Search is not { } search || search.Query.Length == 0)
        {
            SearchStatus.Text = string.Empty;
            return;
        }

        if (_revealFirst && search.Current is { } first)
        {
            _revealFirst = false;
            Viewer.Reveal(first.PageIndex, first.Bounds);
        }

        SearchStatus.Text = $"{search.CurrentIndex + 1} of {search.MatchCount}{(search.IsSearchComplete ? "" : "+")}";
    }

    private void StepMatch(bool previous)
    {
        if (Viewer.Search is { } search && (previous ? search.Previous() : search.Next()) is { } match)
        {
            Viewer.Reveal(match.PageIndex, match.Bounds);
            ShowSearch();
        }
    }

    private void OnSearchKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (Viewer.Search?.Query == SearchBox.Text)
            {
                StepMatch(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            }
            else
            {
                Find();
            }

            e.Handled = true;
        }
    }

    private void OnPageKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (int.TryParse(PageNumber.Text, out int page) && page >= 1 && page <= Viewer.State.PageCount)
            {
                Viewer.GoToPage(page - 1);
                Viewer.Focus();
            }
            else
            {
                Report("Enter a page number between 1 and " + Viewer.State.PageCount.ToString(CultureInfo.CurrentCulture) + ".");
            }

            e.Handled = true;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            await RunAsync(() => OpenAsync(files[0]));
        }

        e.Handled = true;
    }

    private void OnActionRequested(object? sender, ViewerActionEventArgs e)
    {
        if (e.Uri is not { IsAbsoluteUri: true } uri || uri.Scheme is not ("https" or "http"))
        {
            if (e.IsScript)
            {
                Report("PDF scripts are not executed.");
            }

            return;
        }

        if (MessageBox.Show(this, "Open this address in your browser?\n\n" + uri.AbsoluteUri, "External link",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                Report("Could not open the browser: " + exception.Message);
            }
        }
    }

    private async void OnProperties(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (_file is { } file)
        {
            string details = await Task.Run(() =>
            {
                using IDisposable edit = file.Session.BeginEdit();
                PdfDocument document = file.Session.Document;
                return $"{file.Path}\n\nPages: {document.Pages.Count}\nPDF version: {document.Version}\nEncrypted: {document.IsEncrypted}\n\nDiagnostics:\n"
                    + string.Join(Environment.NewLine, document.Diagnostics.Select(diagnostic => diagnostic.ToString()));
            });
            var text = new TextBox { Text = details, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(12) };
            new Window { Owner = this, Title = "Document properties and diagnostics", Width = 640, Height = 480, Content = text, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog();
        }
    });

    private void OnAbout(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        "SneakyPdf\nA PDF reader for Windows, built with the managed Broadside PDF library.\n\nMIT licensed. See LICENSE.txt beside the executable.\nJavaScript, XFA rendering, OCR and printing are not supported.", "About SneakyPdf");

    private void OnSidebar(object sender, RoutedEventArgs e) { _settings.SidebarVisible = SidebarMenu.IsChecked; ApplyPreferences(); _navigation.ShowPage(Viewer.State.PageIndex, Sidebar.SelectedIndex == 1 && _settings.SidebarVisible); }
    private void OnDarkMode(object sender, RoutedEventArgs e) { _settings.DarkMode = DarkMenu.IsChecked; ApplyPreferences(); _navigation.RefreshThumbnails(); }
    private void OnSidebarTab(object sender, SelectionChangedEventArgs e) => _navigation?.ShowPage(Viewer.State.PageIndex, Sidebar.SelectedIndex == 1 && _settings.SidebarVisible);
    private void OnOutlineSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is OutlineEntry { Target: { } target })
        {
            Viewer.GoToPage(target.PageIndex);
        }
    }

    private void OnThumbnailSelected(object sender, SelectionChangedEventArgs e)
    {
        if (PagesList.SelectedIndex >= 0 && PagesList.IsKeyboardFocusWithin)
        {
            Viewer.GoToPage(PagesList.SelectedIndex);
        }
    }
    private void OnExit(object sender, RoutedEventArgs e) => Close();
    private void OnPreviousPage(object sender, RoutedEventArgs e) => Viewer.PreviousPage();
    private void OnNextPage(object sender, RoutedEventArgs e) => Viewer.NextPage();
    private void OnZoomOut(object sender, RoutedEventArgs e) => Viewer.ZoomBy(1 / 1.25);
    private void OnZoomIn(object sender, RoutedEventArgs e) => Viewer.ZoomBy(1.25);
    private void OnFitWidth(object sender, RoutedEventArgs e) => Viewer.ZoomTo(ZoomMode.FitWidth);
    private void OnFitPage(object sender, RoutedEventArgs e) => Viewer.ZoomTo(ZoomMode.FitPage);
    private void OnActualSize(object sender, RoutedEventArgs e) => Viewer.ZoomTo(ZoomMode.ActualSize);
    private void OnFind(object sender, RoutedEventArgs e) => Find();
    private void OnPreviousMatch(object sender, RoutedEventArgs e) => StepMatch(true);
    private void OnNextMatch(object sender, RoutedEventArgs e) => StepMatch(false);
}
