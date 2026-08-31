using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhatsAppChatViewer.Controls;
using WhatsAppChatViewer.Models;
using WhatsAppChatViewer.Services;
using WhatsAppChatViewer.ViewModels;

namespace WhatsAppChatViewer.Views;

public sealed partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new();
    private readonly ListBox _chatList;
    private readonly TextBox _searchBox;
    private readonly Button _jumpMenuButton;
    private readonly ScrollBar _timelineScrollBar;
    private readonly TextBlock _currentDateText;
    private readonly Border _currentDatePill;
    private readonly ChatWallpaperControl _chatWallpaper;
    private ScrollViewer? _chatScrollViewer;
    private VirtualizingStackPanel? _timelinePanel;
    private bool _initialized;
    private bool _suppressTimelineScrollBar;
    private bool _timelineScrollInteraction;
    private bool _indexScrollScheduled;
    private int _pendingTimelineIndex = -1;
    private int _autoScrollGeneration;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _chatList = Require<ListBox>("ChatList");
        _searchBox = Require<TextBox>("SearchBox");
        _jumpMenuButton = Require<Button>("JumpMenuButton");
        _timelineScrollBar = Require<ScrollBar>("TimelineScrollBar");
        _currentDateText = Require<TextBlock>("CurrentDateText");
        _currentDatePill = Require<Border>("CurrentDatePill");
        _chatWallpaper = Require<ChatWallpaperControl>("ChatWallpaper");
        DataContext = _viewModel;

        _viewModel.ScrollRequested += ScrollToItem;
        _viewModel.TimelineReady += ScrollToLatest;
        _viewModel.AttachmentOpenRequested += OpenAttachment;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        _timelineScrollBar.PropertyChanged += TimelineScrollBar_PropertyChanged;
        _timelineScrollBar.AddHandler(
            InputElement.PointerPressedEvent,
            TimelineScrollBar_PointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _timelineScrollBar.AddHandler(
            InputElement.PointerReleasedEvent,
            TimelineScrollBar_PointerReleased,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _timelineScrollBar.PointerCaptureLost += TimelineScrollBar_PointerCaptureLost;
        _chatList.AddHandler(
            InputElement.PointerPressedEvent,
            ChatList_PointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _chatList.AddHandler(
            InputElement.PointerWheelChangedEvent,
            ChatList_PointerWheelChanged,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _chatList.AttachedToVisualTree += (_, _) =>
            Dispatcher.UIThread.Post(ConnectChatScrollViewer, DispatcherPriority.Loaded);

        Opened += MainWindow_Opened;
        KeyDown += MainWindow_KeyDown;
        Closing += (_, _) => _viewModel.Dispose();
    }

    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _searchBox.Focus();
            _searchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _searchBox.IsFocused &&
                 !string.IsNullOrEmpty(_viewModel.SearchText))
        {
            _viewModel.SearchText = string.Empty;
            e.Handled = true;
        }
    }

    private async void MainWindow_Opened(object? sender, EventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        ConnectChatScrollViewer();
        var discovered = ArchiveDiscovery.FindCandidates(
            Program.Arguments,
            Environment.CurrentDirectory,
            AppContext.BaseDirectory);
        var selected = await ConfirmArchiveSelectionAsync(discovered);
        if (selected.Count > 0)
        {
            await _viewModel.LoadArchivesAsync(selected, replaceExisting: true);
        }
    }

    private async void OpenArchive_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open WhatsApp chat export",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("WhatsApp export ZIP")
                {
                    Patterns = ["*.zip"],
                    MimeTypes = ["application/zip", "application/x-zip-compressed"]
                }
            ]
        });

        var paths = files
            .Select(static file => file.TryGetLocalPath())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path!)
            .ToArray();
        var selected = await ConfirmArchiveSelectionAsync(paths);
        if (selected.Count > 0)
        {
            await _viewModel.LoadArchivesAsync(selected, replaceExisting: false);
        }
    }

    private async Task<IReadOnlyList<string>> ConfirmArchiveSelectionAsync(
        IReadOnlyList<string> archivePaths)
    {
        if (archivePaths.Count <= 1)
        {
            return archivePaths;
        }

        var dialog = new ArchiveSelectionWindow(archivePaths);
        WindowPlacement.CenterWithinOwnerScreen(dialog, this);
        return await dialog.ShowDialog<IReadOnlyList<string>?>(this) ?? [];
    }

    private void ScrollToBottom_Click(object? sender, RoutedEventArgs e) =>
        _viewModel.ScrollToBottom();

    private void Jump_Click(object? sender, RoutedEventArgs e) =>
        _jumpMenuButton.Flyout?.Hide();

    private void DismissError_Click(object? sender, RoutedEventArgs e) =>
        _viewModel.DismissError();

    private void ScrollToLatest()
    {
        var generation = ++_autoScrollGeneration;
        UpdateTimelineRange();
        Dispatcher.UIThread.Post(
            () =>
            {
                if (generation != _autoScrollGeneration || _viewModel.Items.Count == 0)
                {
                    return;
                }

                ScrollToIndex(_viewModel.Items.Count - 1);
            },
            DispatcherPriority.Loaded);
    }

    private void ScrollToItem(ChatDisplayItemViewModel item)
    {
        CancelPendingAutoScroll();
        Dispatcher.UIThread.Post(
            () =>
            {
                var index = FindItemIndex(item);
                if (index >= 0)
                {
                    ScrollToIndex(index);
                }
            },
            DispatcherPriority.Loaded);
    }

    private void ScrollToIndex(int index)
    {
        if (_viewModel.Items.Count == 0)
        {
            return;
        }

        index = Math.Clamp(index, 0, _viewModel.Items.Count - 1);
        SetTimelineScrollBarValue(index);
        UpdateCurrentDate(index);
        _chatList.ScrollIntoView(index);
    }

    private void OpenAttachment(AttachmentOpenRequest request)
    {
        if (_viewModel.Media is null)
        {
            return;
        }

        var preview = new MediaPreviewWindow(
            _viewModel.Media,
            request.Attachment,
            _viewModel.MediaGallery,
            request.StartFullscreen,
            request.VideoState);
        WindowPlacement.CenterWithinOwnerScreen(preview, this);
        _ = preview.ShowDialog(this);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.Items))
        {
            CancelPendingAutoScroll();
            UpdateTimelineRange();
            Dispatcher.UIThread.Post(UpdateViewportState, DispatcherPriority.Loaded);
        }
    }

    private void ConnectChatScrollViewer()
    {
        var scrollViewer = _chatList
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault();
        if (ReferenceEquals(scrollViewer, _chatScrollViewer) && _timelinePanel is not null)
        {
            return;
        }

        if (_chatScrollViewer is not null)
        {
            _chatScrollViewer.ScrollChanged -= ChatScrollViewer_ScrollChanged;
        }

        _chatScrollViewer = scrollViewer;
        _timelinePanel = _chatList
            .GetVisualDescendants()
            .OfType<VirtualizingStackPanel>()
            .FirstOrDefault();
        if (_chatScrollViewer is not null)
        {
            _chatScrollViewer.ScrollChanged += ChatScrollViewer_ScrollChanged;
            _chatWallpaper.ScrollOffset = _chatScrollViewer.Offset.Y;
            UpdateViewportState();
        }
    }

    private void ChatScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_chatScrollViewer is null)
        {
            return;
        }

        _chatWallpaper.ScrollOffset = _chatScrollViewer.Offset.Y;
        UpdateViewportState();
    }

    private void ChatList_PointerPressed(object? sender, PointerPressedEventArgs e) =>
        CancelPendingAutoScroll();

    private void ChatList_PointerWheelChanged(object? sender, PointerWheelEventArgs e) =>
        CancelPendingAutoScroll();

    private void TimelineScrollBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        CancelPendingAutoScroll();
        _timelineScrollInteraction = true;
    }

    private void TimelineScrollBar_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _timelineScrollInteraction = false;
        Dispatcher.UIThread.Post(UpdateViewportState, DispatcherPriority.Loaded);
    }

    private void TimelineScrollBar_PointerCaptureLost(
        object? sender,
        PointerCaptureLostEventArgs e)
    {
        _timelineScrollInteraction = false;
        Dispatcher.UIThread.Post(UpdateViewportState, DispatcherPriority.Loaded);
    }

    private void TimelineScrollBar_PropertyChanged(
        object? sender,
        AvaloniaPropertyChangedEventArgs e)
    {
        if (_suppressTimelineScrollBar || e.Property != RangeBase.ValueProperty)
        {
            return;
        }

        CancelPendingAutoScroll();
        _pendingTimelineIndex = (int)Math.Round(_timelineScrollBar.Value);
        UpdateCurrentDate(_pendingTimelineIndex);
        if (_indexScrollScheduled)
        {
            return;
        }

        _indexScrollScheduled = true;
        Dispatcher.UIThread.Post(ApplyPendingTimelineIndex, DispatcherPriority.Loaded);
    }

    private void ApplyPendingTimelineIndex()
    {
        _indexScrollScheduled = false;
        var index = _pendingTimelineIndex;
        _pendingTimelineIndex = -1;
        if (index < 0 || _viewModel.Items.Count == 0)
        {
            return;
        }

        index = Math.Clamp(index, 0, _viewModel.Items.Count - 1);
        _chatList.ScrollIntoView(index);
        if (_pendingTimelineIndex >= 0 && !_indexScrollScheduled)
        {
            _indexScrollScheduled = true;
            Dispatcher.UIThread.Post(ApplyPendingTimelineIndex, DispatcherPriority.Loaded);
        }
    }

    private void UpdateTimelineRange()
    {
        _suppressTimelineScrollBar = true;
        _timelineScrollBar.Maximum = Math.Max(0, _viewModel.Items.Count - 1);
        _timelineScrollBar.IsEnabled = _viewModel.Items.Count > 1;
        if (_viewModel.Items.Count == 0)
        {
            _timelineScrollBar.Value = 0;
            _currentDateText.Text = string.Empty;
            _chatWallpaper.ScrollOffset = 0;
        }
        else if (_timelineScrollBar.Value > _timelineScrollBar.Maximum)
        {
            _timelineScrollBar.Value = _timelineScrollBar.Maximum;
        }

        _suppressTimelineScrollBar = false;
    }

    private void UpdateViewportState()
    {
        var index = FindFirstVisibleIndex();
        if (index < 0)
        {
            return;
        }

        UpdateCurrentDate(index);
        UpdateCurrentDatePillOpacity();
        if (!_timelineScrollInteraction)
        {
            SetTimelineScrollBarValue(index);
        }
    }

    private int FindFirstVisibleIndex()
    {
        var bestIndex = -1;
        var bestTop = double.PositiveInfinity;
        if (_timelinePanel is null)
        {
            return -1;
        }

        foreach (var container in _timelinePanel.Children.OfType<ListBoxItem>())
        {
            if (container.TranslatePoint(default, _chatList) is not { } point)
            {
                continue;
            }

            var bottom = point.Y + container.Bounds.Height;
            if (bottom <= 0 || point.Y >= _chatList.Bounds.Height || point.Y >= bestTop)
            {
                continue;
            }

            var index = _chatList.IndexFromContainer(container);
            if (index >= 0)
            {
                bestIndex = index;
                bestTop = point.Y;
            }
        }

        return bestIndex;
    }

    private void UpdateCurrentDatePillOpacity()
    {
        if (_timelinePanel is null)
        {
            _currentDatePill.Opacity = 1;
            return;
        }

        var separatorNearTop = _timelinePanel.Children
            .OfType<ListBoxItem>()
            .Any(container =>
                container.DataContext is ChatDisplayItemViewModel { IsDateSeparator: true } &&
                container.TranslatePoint(default, _chatList) is { } point &&
                point.Y + container.Bounds.Height > 0 &&
                point.Y < 54);
        _currentDatePill.Opacity = separatorNearTop ? 0 : 1;
    }

    private int FindItemIndex(ChatDisplayItemViewModel item)
    {
        for (var index = 0; index < _viewModel.Items.Count; index++)
        {
            if (ReferenceEquals(_viewModel.Items[index], item))
            {
                return index;
            }
        }

        return -1;
    }

    private void UpdateCurrentDate(int itemIndex)
    {
        if (itemIndex < 0 || itemIndex >= _viewModel.Items.Count)
        {
            return;
        }

        var label = _viewModel.Items[itemIndex].PersistentDateLabel;
        if (!string.IsNullOrWhiteSpace(label))
        {
            _currentDateText.Text = label;
        }
    }

    private void SetTimelineScrollBarValue(double value)
    {
        _suppressTimelineScrollBar = true;
        _timelineScrollBar.Value = Math.Clamp(
            value,
            _timelineScrollBar.Minimum,
            _timelineScrollBar.Maximum);
        _suppressTimelineScrollBar = false;
    }

    private void CancelPendingAutoScroll() => _autoScrollGeneration++;

    private T Require<T>(string name) where T : Control =>
        this.FindControl<T>(name) ?? throw new InvalidOperationException($"{name} was not created.");
}
