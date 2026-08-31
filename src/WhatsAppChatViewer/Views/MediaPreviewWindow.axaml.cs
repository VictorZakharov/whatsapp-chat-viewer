using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhatsAppChatViewer.Controls;
using WhatsAppChatViewer.Models;
using WhatsAppChatViewer.Services;
using WhatsAppChatViewer.ViewModels;

namespace WhatsAppChatViewer.Views;

public sealed partial class MediaPreviewWindow : Window
{
    private ArchiveMediaService? _archive;
    private ArchiveAttachment? _attachment;
    private readonly Grid _chromeHeader;
    private readonly TextBlock _currentMediaDateText;
    private readonly Grid _previewSurface;
    private readonly Image _fullImage;
    private readonly LazyArchiveVideoPlayer _videoPlayer;
    private readonly StackPanel _genericPreview;
    private readonly Button _previousButton;
    private readonly Button _nextButton;
    private readonly Border _galleryStripContainer;
    private readonly ListBox _galleryStrip;
    private readonly Button _expandGalleryButton;
    private readonly Border _galleryDrawer;
    private readonly ListBox _galleryDrawerRows;
    private readonly TextBlock _galleryDrawerCountText;
    private readonly Border _galleryDrawerLoading;
    private readonly Grid _footerBar;
    private readonly Border _busyOverlay;
    private readonly ProgressBar _operationProgress;
    private readonly TextBlock _operationText;
    private readonly TextBlock _galleryPositionText;
    private readonly List<MediaGalleryItemViewModel> _galleryItems = [];
    private IReadOnlyList<MediaGalleryItemViewModel[]> _galleryDrawerRowsSource = [];
    private Bitmap? _bitmap;
    private CancellationTokenSource? _previewCancellation;
    private CancellationTokenSource? _operationCancellation;
    private int _previewGeneration;
    private int _currentGalleryIndex = -1;
    private bool _suppressGallerySelection;
    private bool _galleryPointerDown;
    private bool _galleryDragged;
    private bool _isGalleryDrawerExpanded;
    private bool _isFullscreen;
    private bool _startFullscreen;
    private string? _initialVideoEntryName;
    private VideoPlaybackState? _initialVideoState;
    private int _pendingGallerySelection = -1;
    private Point _galleryDragStart;
    private Vector _galleryDragStartOffset;
    private IPointer? _galleryDragPointer;
    private ScrollViewer? _galleryScrollViewer;
    private int _galleryDrawerColumnCount;
    private int _galleryDrawerLayoutGeneration;
    private WindowState _restoreWindowState = WindowState.Normal;

    public MediaPreviewWindow()
    {
        AvaloniaXamlLoader.Load(this);

        _chromeHeader = Require<Grid>("ChromeHeader");
        _currentMediaDateText = Require<TextBlock>("CurrentMediaDateText");
        _previewSurface = Require<Grid>("PreviewSurface");
        _fullImage = Require<Image>("FullImage");
        _videoPlayer = Require<LazyArchiveVideoPlayer>("VideoPlayer");
        _genericPreview = Require<StackPanel>("GenericPreview");
        _previousButton = Require<Button>("PreviousButton");
        _nextButton = Require<Button>("NextButton");
        _galleryStripContainer = Require<Border>("GalleryStripContainer");
        _galleryStrip = Require<ListBox>("GalleryStrip");
        _expandGalleryButton = Require<Button>("ExpandGalleryButton");
        _galleryDrawer = Require<Border>("GalleryDrawer");
        _galleryDrawerRows = Require<ListBox>("GalleryDrawerRows");
        _galleryDrawerCountText = Require<TextBlock>("GalleryDrawerCountText");
        _galleryDrawerLoading = Require<Border>("GalleryDrawerLoading");
        _footerBar = Require<Grid>("FooterBar");
        _busyOverlay = Require<Border>("BusyOverlay");
        _operationProgress = Require<ProgressBar>("OperationProgress");
        _operationText = Require<TextBlock>("OperationText");
        _galleryPositionText = Require<TextBlock>("GalleryPositionText");

        _videoPlayer.FullscreenRequested += (_, _) => ToggleFullscreen();
        _videoPlayer.PreviousRequested += (_, _) => NavigateMedia(-1);
        _videoPlayer.NextRequested += (_, _) => NavigateMedia(1);
        _galleryStrip.AddHandler(
            InputElement.PointerPressedEvent,
            GalleryStrip_PointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _galleryStrip.AddHandler(
            InputElement.PointerMovedEvent,
            GalleryStrip_PointerMoved,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _galleryStrip.AddHandler(
            InputElement.PointerReleasedEvent,
            GalleryStrip_PointerReleased,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _galleryStrip.AddHandler(
            InputElement.PointerWheelChangedEvent,
            GalleryStrip_PointerWheelChanged,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _galleryStrip.PointerCaptureLost += GalleryStrip_PointerCaptureLost;
        Opened += MediaPreviewWindow_Opened;
        Closed += MediaPreviewWindow_Closed;
        KeyDown += MediaPreviewWindow_KeyDown;
        SizeChanged += MediaPreviewWindow_SizeChanged;
    }

    public MediaPreviewWindow(
        ArchiveMediaService archive,
        ArchiveAttachment attachment,
        IReadOnlyList<MediaGalleryEntry> mediaGallery,
        bool startFullscreen = false,
        VideoPlaybackState? initialVideoState = null)
        : this()
    {
        _archive = archive;
        _attachment = attachment;
        _startFullscreen = startFullscreen;
        _initialVideoEntryName = attachment.EntryName;
        _initialVideoState = initialVideoState;

        if (IsGalleryMedia(attachment))
        {
            var gallery = mediaGallery
                .Where(static item => IsGalleryMedia(item.Attachment))
                .DistinctBy(static item => item.Attachment.EntryName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!gallery.Any(item =>
                    item.Attachment.EntryName.Equals(attachment.EntryName, StringComparison.OrdinalIgnoreCase)))
            {
                gallery.Add(new MediaGalleryEntry(attachment, null));
            }

            _galleryItems.AddRange(gallery.Select(item =>
                new MediaGalleryItemViewModel(archive, item.Attachment, item.Timestamp)));
            SetCurrentGalleryIndex(_galleryItems.FindIndex(item =>
                item.Attachment.EntryName.Equals(attachment.EntryName, StringComparison.OrdinalIgnoreCase)));
            _galleryStrip.ItemsSource = _galleryItems;
        }

        _galleryDrawerCountText.Text = $"{_galleryItems.Count:N0} media item{(_galleryItems.Count == 1 ? string.Empty : "s")}";

        UpdateChrome();
    }

    private async void MediaPreviewWindow_Opened(object? sender, EventArgs e)
    {
        if (_archive is null || _attachment is null)
        {
            return;
        }

        if (IsGalleryMedia(_attachment))
        {
            await SelectGalleryItemAsync(Math.Max(0, _currentGalleryIndex));
        }
        else
        {
            ShowGenericPreview();
        }

        if (_startFullscreen && !_isFullscreen)
        {
            ToggleFullscreen();
        }
    }

    private void MediaPreviewWindow_Closed(object? sender, EventArgs e)
    {
        _previewGeneration++;
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _fullImage.Source = null;
        _bitmap?.Dispose();
        _videoPlayer.Dispose();
    }

    private async Task SelectGalleryItemAsync(int galleryIndex)
    {
        if (galleryIndex < 0 || galleryIndex >= _galleryItems.Count)
        {
            return;
        }

        _attachment = _galleryItems[galleryIndex].Attachment;
        SetCurrentGalleryIndex(galleryIndex);
        if (_attachment.Kind == AttachmentKind.Video)
        {
            ShowVideo();
            UpdateGalleryState(scrollIntoView: true);
            return;
        }

        await LoadImageAsync(galleryIndex);
    }

    private async Task LoadImageAsync(int galleryIndex)
    {
        if (_archive is null || galleryIndex < 0 || galleryIndex >= _galleryItems.Count)
        {
            return;
        }

        _attachment = _galleryItems[galleryIndex].Attachment;
        SetCurrentGalleryIndex(galleryIndex);
        var generation = ++_previewGeneration;
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = new CancellationTokenSource();
        var cancellationToken = _previewCancellation.Token;

        _videoPlayer.StopPlayback();
        _videoPlayer.IsVisible = false;
        _genericPreview.IsVisible = false;
        _fullImage.IsVisible = false;
        UpdateChrome();
        UpdateGalleryState(scrollIntoView: true);
        SetBusy(true, "Loading image from ZIP…", indeterminate: true);

        Bitmap? loadedBitmap = null;
        try
        {
            loadedBitmap = await _archive.LoadBitmapAsync(_attachment, 2400, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (generation != _previewGeneration)
            {
                return;
            }

            _fullImage.Source = null;
            _bitmap?.Dispose();
            _bitmap = loadedBitmap;
            loadedBitmap = null;
            _fullImage.Source = _bitmap;
            _fullImage.IsVisible = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (generation == _previewGeneration)
            {
                ShowGenericPreview("Preview unavailable — open in the default app.", preserveGallery: true);
            }
        }
        finally
        {
            loadedBitmap?.Dispose();
            if (generation == _previewGeneration)
            {
                SetBusy(false, string.Empty, indeterminate: false);
            }
        }
    }

    private void ShowVideo()
    {
        if (_archive is null || _attachment is null)
        {
            return;
        }

        _previewGeneration++;
        _previewCancellation?.Cancel();
        _fullImage.Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
        _fullImage.IsVisible = false;
        _genericPreview.IsVisible = false;
        _videoPlayer.IsVisible = true;
        var initialState = string.Equals(
            _attachment.EntryName,
            _initialVideoEntryName,
            StringComparison.OrdinalIgnoreCase)
            ? _initialVideoState
            : null;
        _videoPlayer.Configure(
            _archive,
            _attachment,
            autoPlay: true,
            initialState: initialState);
        _initialVideoEntryName = null;
        _initialVideoState = null;
        _videoPlayer.SetFullscreenState(_isFullscreen);
        UpdateChrome();
        UpdateGalleryState(scrollIntoView: true);
    }

    private void ShowGenericPreview(string? overrideText = null, bool preserveGallery = false)
    {
        if (_attachment is null)
        {
            return;
        }

        _videoPlayer.StopPlayback();
        _fullImage.IsVisible = false;
        _videoPlayer.IsVisible = false;
        _genericPreview.IsVisible = true;
        if (preserveGallery)
        {
            UpdateGalleryState(scrollIntoView: true);
        }
        else
        {
            _galleryStripContainer.IsVisible = false;
            _previousButton.IsVisible = false;
            _nextButton.IsVisible = false;
            _videoPlayer.SetNavigationState(false, false);
        }
        Require<TextBlock>("MediaIcon").Text = GetIcon(_attachment.Kind);
        Require<TextBlock>("MediaTypeText").Text = GetTypeText(_attachment.Kind);
        Require<TextBlock>("GenericDescription").Text = overrideText ??
            "This entry stays inside the ZIP until you open or save it.";
        UpdateChrome();
    }

    private void UpdateChrome()
    {
        if (_attachment is null)
        {
            return;
        }

        Require<TextBlock>("FileNameText").Text = _attachment.FileName;
        var currentGalleryItem = _currentGalleryIndex >= 0 && _currentGalleryIndex < _galleryItems.Count
            ? _galleryItems[_currentGalleryIndex]
            : null;
        _currentMediaDateText.Text = currentGalleryItem?.DateText ?? string.Empty;
        _currentMediaDateText.IsVisible = currentGalleryItem?.Timestamp is not null;
        Require<TextBlock>("FileDetailsText").Text = $"{GetTypeText(_attachment.Kind)}  ·  {_attachment.SizeText}";
        Require<TextBlock>("FooterText").Text = IsImage(_attachment)
            ? "Left/Right or Page Up/Page Down to browse  ·  F11 for fullscreen"
            : "Page Up/Page Down to browse  ·  Space to play/pause  ·  Left/Right to seek  ·  R / Shift+R to rotate  ·  F11 for fullscreen";
        Title = _attachment.FileName;
    }

    private void SetCurrentGalleryIndex(int galleryIndex)
    {
        if (_currentGalleryIndex >= 0 && _currentGalleryIndex < _galleryItems.Count)
        {
            _galleryItems[_currentGalleryIndex].IsCurrent = false;
        }

        _currentGalleryIndex = galleryIndex;
        if (_currentGalleryIndex >= 0 && _currentGalleryIndex < _galleryItems.Count)
        {
            _galleryItems[_currentGalleryIndex].IsCurrent = true;
        }
    }

    private void UpdateGalleryState(bool scrollIntoView)
    {
        var showGallery = !_isFullscreen && !_isGalleryDrawerExpanded && _galleryItems.Count > 0 &&
                          _attachment is not null && IsGalleryMedia(_attachment);
        _galleryStripContainer.IsVisible = showGallery;
        _expandGalleryButton.IsVisible = showGallery && _galleryItems.Count > 1;
        if (!showGallery)
        {
            SetGalleryExpandButtonHovered(false);
        }
        var showNavigation = _galleryItems.Count > 1 &&
                             _attachment is not null &&
                             IsGalleryMedia(_attachment);
        var hasPrevious = showNavigation && _currentGalleryIndex > 0;
        var hasNext = showNavigation &&
                      _currentGalleryIndex >= 0 &&
                      _currentGalleryIndex < _galleryItems.Count - 1;
        var videoNavigation = _attachment?.Kind == AttachmentKind.Video;
        _previousButton.IsVisible = showNavigation && !videoNavigation;
        _nextButton.IsVisible = showNavigation && !videoNavigation;
        _previousButton.IsEnabled = hasPrevious;
        _nextButton.IsEnabled = hasNext;
        _videoPlayer.SetNavigationState(
            videoNavigation && hasPrevious,
            videoNavigation && hasNext);
        _galleryPositionText.Text = _galleryItems.Count == 0
            ? string.Empty
            : $"{_currentGalleryIndex + 1:N0} / {_galleryItems.Count:N0}";

        _suppressGallerySelection = true;
        _galleryStrip.SelectedIndex = _currentGalleryIndex;
        _suppressGallerySelection = false;
        if (showGallery && scrollIntoView && _currentGalleryIndex >= 0)
        {
            var item = _galleryItems[_currentGalleryIndex];
            Dispatcher.UIThread.Post(() => _galleryStrip.ScrollIntoView(item), DispatcherPriority.Loaded);
        }
    }

    private async void GalleryStrip_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressGallerySelection || _galleryStrip.SelectedIndex < 0 ||
            _galleryStrip.SelectedIndex == _currentGalleryIndex)
        {
            return;
        }

        if (_galleryPointerDown)
        {
            _pendingGallerySelection = _galleryStrip.SelectedIndex;
            return;
        }

        await SelectGalleryItemAsync(_galleryStrip.SelectedIndex);
    }

    private void GalleryStrip_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_galleryStrip).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // Leave the native scrollbar alone. Pointer interaction over the thumbnail
        // surface is handled here so ListBox does not temporarily select an item
        // underneath a drag and bring that item back into view on release.
        if (IsGalleryScrollBarSource(e.Source))
        {
            return;
        }

        var scrollViewer = GetGalleryScrollViewer();
        if (scrollViewer is null)
        {
            return;
        }

        _galleryPointerDown = true;
        _galleryDragged = false;
        _pendingGallerySelection = GetGalleryItemIndex(e.Source);
        _galleryDragPointer = e.Pointer;
        _galleryDragStart = e.GetPosition(_galleryStrip);
        _galleryDragStartOffset = scrollViewer.Offset;
        e.Pointer.Capture(_galleryStrip);
        e.Handled = true;
    }

    private void GalleryStrip_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_galleryPointerDown ||
            !ReferenceEquals(e.Pointer, _galleryDragPointer) ||
            !e.GetCurrentPoint(_galleryStrip).Properties.IsLeftButtonPressed ||
            GetGalleryScrollViewer() is not { } scrollViewer)
        {
            return;
        }

        var delta = e.GetPosition(_galleryStrip) - _galleryDragStart;
        if (!_galleryDragged && Math.Abs(delta.X) < 6)
        {
            return;
        }

        if (!_galleryDragged)
        {
            _galleryDragged = true;
        }

        SetGalleryHorizontalOffset(scrollViewer, _galleryDragStartOffset.X - delta.X);
        e.Handled = true;
    }

    private void GalleryStrip_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_galleryPointerDown || !ReferenceEquals(e.Pointer, _galleryDragPointer))
        {
            return;
        }

        var wasDragged = _galleryDragged;
        var requestedSelection = _pendingGallerySelection;
        ResetGalleryPointerState(releaseCapture: true);
        e.Handled = true;

        if (!wasDragged && requestedSelection >= 0 && requestedSelection != _currentGalleryIndex)
        {
            _ = SelectGalleryItemAsync(requestedSelection);
        }
    }

    private void GalleryStrip_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (ReferenceEquals(e.Pointer, _galleryDragPointer))
        {
            ResetGalleryPointerState(releaseCapture: false);
        }
    }

    private void GalleryStrip_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (GetGalleryScrollViewer() is not { } scrollViewer)
        {
            return;
        }

        var delta = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y)
            ? e.Delta.X
            : e.Delta.Y;
        if (Math.Abs(delta) < double.Epsilon)
        {
            return;
        }

        SetGalleryHorizontalOffset(scrollViewer, scrollViewer.Offset.X - (delta * 72));
        e.Handled = true;
    }

    private ScrollViewer? GetGalleryScrollViewer() =>
        _galleryScrollViewer ??=
            _galleryStrip.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    private int GetGalleryItemIndex(object? source)
    {
        if (source is not Visual visual)
        {
            return -1;
        }

        var container = visual as ListBoxItem ??
                        visual.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
        return container is null ? -1 : _galleryStrip.IndexFromContainer(container);
    }

    private static bool IsGalleryScrollBarSource(object? source)
    {
        if (source is not Visual visual)
        {
            return false;
        }

        return visual is ScrollBar || visual.GetVisualAncestors().OfType<ScrollBar>().Any();
    }

    private static void SetGalleryHorizontalOffset(ScrollViewer scrollViewer, double offset)
    {
        var maximum = Math.Max(0, scrollViewer.Extent.Width - scrollViewer.Viewport.Width);
        scrollViewer.Offset = new Vector(Math.Clamp(offset, 0, maximum), scrollViewer.Offset.Y);
    }

    private void ResetGalleryPointerState(bool releaseCapture)
    {
        var pointer = _galleryDragPointer;
        _galleryPointerDown = false;
        _galleryDragged = false;
        _pendingGallerySelection = -1;
        _galleryDragPointer = null;
        if (releaseCapture && ReferenceEquals(pointer?.Captured, _galleryStrip))
        {
            pointer.Capture(null);
        }
    }

    private void GalleryStripContainer_PointerEntered(object? sender, PointerEventArgs e) =>
        SetGalleryExpandButtonHovered(true);

    private void GalleryStripContainer_PointerExited(object? sender, PointerEventArgs e) =>
        SetGalleryExpandButtonHovered(false);

    private void SetGalleryExpandButtonHovered(bool isHovered)
    {
        var show = isHovered &&
                   !_isFullscreen &&
                   !_isGalleryDrawerExpanded &&
                   _galleryItems.Count > 1;
        _expandGalleryButton.Opacity = show ? 1 : 0;
        _expandGalleryButton.IsHitTestVisible = show;
    }

    private void ExpandGallery_Click(object? sender, RoutedEventArgs e)
    {
        if (_isFullscreen || _galleryItems.Count <= 1)
        {
            return;
        }

        _isGalleryDrawerExpanded = true;
        _galleryDrawer.IsVisible = true;
        _galleryDrawerLoading.IsVisible = true;
        SetGalleryExpandButtonHovered(false);
        UpdateGalleryState(scrollIntoView: false);
        var layoutGeneration = ++_galleryDrawerLayoutGeneration;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!_isGalleryDrawerExpanded || layoutGeneration != _galleryDrawerLayoutGeneration)
                {
                    return;
                }

                RebuildGalleryDrawerRows(scrollCurrentIntoView: true);
                DispatcherTimer.RunOnce(
                    () =>
                    {
                        if (_isGalleryDrawerExpanded && layoutGeneration == _galleryDrawerLayoutGeneration)
                        {
                            _galleryDrawerLoading.IsVisible = false;
                        }
                    },
                    TimeSpan.FromMilliseconds(80),
                    DispatcherPriority.Background);
            },
            DispatcherPriority.Background);
        e.Handled = true;
    }

    private void CollapseGallery_Click(object? sender, RoutedEventArgs e)
    {
        CollapseGalleryDrawer(scrollStripIntoView: true);
        e.Handled = true;
    }

    private void GalleryDrawerItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MediaGalleryItemViewModel item })
        {
            return;
        }

        var galleryIndex = _galleryItems.IndexOf(item);
        if (galleryIndex < 0)
        {
            return;
        }

        var isCurrentItem = galleryIndex == _currentGalleryIndex;
        CollapseGalleryDrawer(scrollStripIntoView: isCurrentItem);
        if (!isCurrentItem)
        {
            _ = SelectGalleryItemAsync(galleryIndex);
        }

        e.Handled = true;
    }

    private void CollapseGalleryDrawer(bool scrollStripIntoView)
    {
        if (!_isGalleryDrawerExpanded)
        {
            return;
        }

        _isGalleryDrawerExpanded = false;
        _galleryDrawerLayoutGeneration++;
        _galleryDrawer.IsVisible = false;
        _galleryDrawerLoading.IsVisible = false;
        _galleryDrawerRows.ItemsSource = null;
        _galleryDrawerRowsSource = [];
        _galleryDrawerColumnCount = 0;
        UpdateGalleryState(scrollIntoView: scrollStripIntoView);
        Focus();
    }

    private void RebuildGalleryDrawerRows(bool scrollCurrentIntoView)
    {
        if (!_isGalleryDrawerExpanded || _galleryItems.Count == 0)
        {
            return;
        }

        const double drawerItemWidth = 90;
        const double drawerHorizontalChrome = 48;
        var availableWidth = Math.Max(drawerItemWidth, ClientSize.Width - drawerHorizontalChrome);
        var columnCount = Math.Max(1, (int)Math.Floor(availableWidth / drawerItemWidth));
        if (columnCount != _galleryDrawerColumnCount || _galleryDrawerRowsSource.Count == 0)
        {
            _galleryDrawerColumnCount = columnCount;
            _galleryDrawerRowsSource = _galleryItems
                .Chunk(columnCount)
                .ToArray();
            _galleryDrawerRows.ItemsSource = _galleryDrawerRowsSource;
        }

        if (!scrollCurrentIntoView || _currentGalleryIndex < 0)
        {
            return;
        }

        var currentRowIndex = _currentGalleryIndex / columnCount;
        if (currentRowIndex < 0 || currentRowIndex >= _galleryDrawerRowsSource.Count)
        {
            return;
        }

        var currentRow = _galleryDrawerRowsSource[currentRowIndex];
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_isGalleryDrawerExpanded)
                {
                    _galleryDrawerRows.ScrollIntoView(currentRow);
                }
            },
            DispatcherPriority.Loaded);
    }

    private void MediaPreviewWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_isGalleryDrawerExpanded)
        {
            var layoutGeneration = ++_galleryDrawerLayoutGeneration;
            DispatcherTimer.RunOnce(
                () =>
                {
                    if (_isGalleryDrawerExpanded && layoutGeneration == _galleryDrawerLayoutGeneration)
                    {
                        RebuildGalleryDrawerRows(scrollCurrentIntoView: true);
                    }
                },
                TimeSpan.FromMilliseconds(90),
                DispatcherPriority.Background);
        }
    }

    private void Previous_Click(object? sender, RoutedEventArgs e) => NavigateMedia(-1);

    private void Next_Click(object? sender, RoutedEventArgs e) => NavigateMedia(1);

    private void NavigateMedia(int offset)
    {
        var target = _currentGalleryIndex + offset;
        if (target >= 0 && target < _galleryItems.Count)
        {
            _ = SelectGalleryItemAsync(target);
        }
    }

    private void MediaPreviewWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_isGalleryDrawerExpanded)
            {
                CollapseGalleryDrawer(scrollStripIntoView: true);
            }
            else if (_isFullscreen)
            {
                ExitFullscreen();
            }
            else
            {
                Close();
            }

            e.Handled = true;
            return;
        }

        if (e.Key is Key.F or Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }

        if (_attachment is not null && IsImage(_attachment))
        {
            switch (e.Key)
            {
                case Key.Left:
                case Key.PageUp:
                    NavigateMedia(-1);
                    e.Handled = true;
                    break;
                case Key.Right:
                case Key.PageDown:
                    NavigateMedia(1);
                    e.Handled = true;
                    break;
            }
        }
        else if (_attachment?.Kind == AttachmentKind.Video)
        {
            switch (e.Key)
            {
                case Key.PageUp:
                    NavigateMedia(-1);
                    e.Handled = true;
                    break;
                case Key.PageDown:
                    NavigateMedia(1);
                    e.Handled = true;
                    break;
                case Key.Space:
                    _videoPlayer.TogglePlayPause();
                    e.Handled = true;
                    break;
                case Key.Left:
                    _videoPlayer.SeekRelative(TimeSpan.FromSeconds(-5));
                    e.Handled = true;
                    break;
                case Key.Right:
                    _videoPlayer.SeekRelative(TimeSpan.FromSeconds(5));
                    e.Handled = true;
                    break;
                case Key.M:
                    _videoPlayer.ToggleMute();
                    e.Handled = true;
                    break;
                case Key.R:
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    {
                        _videoPlayer.RotateCounterclockwise();
                    }
                    else
                    {
                        _videoPlayer.RotateClockwise();
                    }

                    e.Handled = true;
                    break;
            }
        }
    }

    private void ToggleFullscreen_Click(object? sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        if (_isFullscreen)
        {
            ExitFullscreen();
            return;
        }

        if (_isGalleryDrawerExpanded)
        {
            CollapseGalleryDrawer(scrollStripIntoView: false);
        }

        _restoreWindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState;
        _isFullscreen = true;
        _chromeHeader.IsVisible = false;
        _galleryStripContainer.IsVisible = false;
        _footerBar.IsVisible = false;
        Grid.SetRow(_previewSurface, 0);
        Grid.SetRowSpan(_previewSurface, 4);
        _previewSurface.Margin = new Thickness(0);
        WindowState = WindowState.FullScreen;
        _videoPlayer.SetFullscreenState(true);
        Dispatcher.UIThread.Post(_videoPlayer.RefreshVideoSurface, DispatcherPriority.Loaded);
    }

    private void ExitFullscreen()
    {
        _isFullscreen = false;
        WindowState = _restoreWindowState == WindowState.FullScreen
            ? WindowState.Normal
            : _restoreWindowState;
        _chromeHeader.IsVisible = true;
        _footerBar.IsVisible = true;
        Grid.SetRow(_previewSurface, 1);
        Grid.SetRowSpan(_previewSurface, 1);
        _previewSurface.Margin = new Thickness(18);
        UpdateGalleryState(scrollIntoView: true);
        _videoPlayer.SetFullscreenState(false);
        Dispatcher.UIThread.Post(_videoPlayer.RefreshVideoSurface, DispatcherPriority.Loaded);
    }

    private async void OpenExternal_Click(object? sender, RoutedEventArgs e)
    {
        if (_busyOverlay.IsVisible || _archive is null || _attachment is null)
        {
            return;
        }

        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        var progress = new Progress<double>(value =>
        {
            _operationProgress.IsIndeterminate = false;
            _operationProgress.Value = value * 100;
            _operationText.Text = $"Preparing selected entry… {value:P0}";
        });

        SetBusy(true, "Preparing selected entry…", indeterminate: false);
        try
        {
            var path = await _archive.CreateTemporaryCopyAsync(
                _attachment,
                progress,
                _operationCancellation.Token);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await ShowOperationErrorAsync(exception.Message);
        }
        finally
        {
            SetBusy(false, string.Empty, indeterminate: false);
        }
    }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (_archive is null || _attachment is null)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save attachment",
            SuggestedFileName = _attachment.FileName,
            ShowOverwritePrompt = true
        });
        if (file is null)
        {
            return;
        }

        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        var progress = new Progress<double>(value =>
        {
            _operationProgress.IsIndeterminate = false;
            _operationProgress.Value = value * 100;
            _operationText.Text = $"Saving… {value:P0}";
        });

        SetBusy(true, "Saving selected entry…", indeterminate: false);
        try
        {
            await using var output = await file.OpenWriteAsync();
            if (output.CanSeek)
            {
                output.SetLength(0);
            }

            await _archive.CopyToAsync(_attachment, output, progress, _operationCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await ShowOperationErrorAsync(exception.Message);
        }
        finally
        {
            SetBusy(false, string.Empty, indeterminate: false);
        }
    }

    private async Task ShowOperationErrorAsync(string message)
    {
        _operationText.Text = message;
        await Task.Delay(1800);
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private void SetBusy(bool busy, string text, bool indeterminate)
    {
        _busyOverlay.IsVisible = busy;
        _operationText.Text = text;
        _operationProgress.IsIndeterminate = indeterminate;
        if (!indeterminate)
        {
            _operationProgress.Value = 0;
        }
    }

    private T Require<T>(string name) where T : Control =>
        this.FindControl<T>(name) ?? throw new InvalidOperationException($"{name} was not created.");

    private static bool IsImage(ArchiveAttachment attachment) =>
        attachment.Kind is AttachmentKind.Image or AttachmentKind.Sticker or AttachmentKind.AnimatedImage;

    private static bool IsGalleryMedia(ArchiveAttachment attachment) =>
        IsImage(attachment) || attachment.Kind == AttachmentKind.Video;

    private static string GetIcon(AttachmentKind kind) => kind switch
    {
        AttachmentKind.Video => "▶",
        AttachmentKind.Audio => "♪",
        AttachmentKind.Document => "▤",
        AttachmentKind.Contact => "♙",
        _ => "↗"
    };

    private static string GetTypeText(AttachmentKind kind) => kind switch
    {
        AttachmentKind.Image => "Image",
        AttachmentKind.Sticker => "Sticker",
        AttachmentKind.AnimatedImage => "Animated image",
        AttachmentKind.Video => "Video",
        AttachmentKind.Audio => "Audio",
        AttachmentKind.Document => "Document",
        AttachmentKind.Contact => "Contact card",
        _ => "Attachment"
    };
}
