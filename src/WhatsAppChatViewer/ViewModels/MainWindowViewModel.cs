using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Threading;
using WhatsAppChatViewer.Models;
using WhatsAppChatViewer.Services;

namespace WhatsAppChatViewer.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly WhatsAppArchiveLoader _loader = new();
    private ChatSession? _session;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _searchCancellation;
    private IReadOnlyList<ChatDisplayItemViewModel> _items = [];
    private IReadOnlyList<ChatDisplayItemViewModel> _allItems = [];
    private ChatDisplayItemViewModel[] _messageItems = [];
    private IReadOnlyList<MediaGalleryEntry> _mediaGallery = [];
    private ChatListItemViewModel? _selectedChat;
    private string _title = "WhatsApp archive";
    private string _subtitle = "Open an exported ZIP to begin";
    private string _archiveName = "No export open";
    private string _archiveDetails = "ZIP entries are read in place";
    private string _messageCountText = "—";
    private string _mediaCountText = "—";
    private string _rangeText = "—";
    private string _statusText = "Ready";
    private string _errorText = string.Empty;
    private string _searchText = string.Empty;
    private string _searchResultText = string.Empty;
    private bool _hasChat;
    private bool _isBusy;
    private string? _selectedIdentity;
    private DateTimeOffset? _jumpDate;
    private TimeSpan? _jumpTime;
    private ChatDisplayItemViewModel? _selectedTimelineItem;
    private int _highlightGeneration;
    private int _loadGeneration;

    public MainWindowViewModel()
    {
        Chats = new ObservableCollection<ChatListItemViewModel>();
        Participants = new ObservableCollection<string>();
        JumpCommand = new RelayCommand(JumpToSelectedTime, () => HasChat);
    }

    public event Action<ChatDisplayItemViewModel>? ScrollRequested;
    public event Action<AttachmentOpenRequest>? AttachmentOpenRequested;
    public event Action? TimelineReady;

    public ObservableCollection<ChatListItemViewModel> Chats { get; }
    public ObservableCollection<string> Participants { get; }

    public IReadOnlyList<ChatDisplayItemViewModel> Items
    {
        get => _items;
        private set => SetProperty(ref _items, value);
    }

    public ChatListItemViewModel? SelectedChat
    {
        get => _selectedChat;
        set
        {
            if (SetProperty(ref _selectedChat, value))
            {
                ActivateChat(value);
            }
        }
    }

    public ArchiveMediaService? Media => _session?.Media;
    public IReadOnlyList<MediaGalleryEntry> MediaGallery => _mediaGallery;
    public ICommand JumpCommand { get; }
    public string Title { get => _title; private set => SetProperty(ref _title, value); }
    public string Subtitle { get => _subtitle; private set => SetProperty(ref _subtitle, value); }
    public string ArchiveName { get => _archiveName; private set => SetProperty(ref _archiveName, value); }
    public string ArchiveDetails { get => _archiveDetails; private set => SetProperty(ref _archiveDetails, value); }
    public string MessageCountText { get => _messageCountText; private set => SetProperty(ref _messageCountText, value); }
    public string MediaCountText { get => _mediaCountText; private set => SetProperty(ref _mediaCountText, value); }
    public string RangeText { get => _rangeText; private set => SetProperty(ref _rangeText, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetProperty(ref _errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public bool HasChat
    {
        get => _hasChat;
        private set
        {
            if (SetProperty(ref _hasChat, value))
            {
                OnPropertyChanged(nameof(IsWelcomeVisible));
                ((RelayCommand)JumpCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsWelcomeVisible));
            }
        }
    }

    public bool IsWelcomeVisible => !HasChat && !IsBusy;
    public bool HasSearchResultText => !string.IsNullOrWhiteSpace(SearchResultText);
    public string ContactInitial => string.IsNullOrWhiteSpace(Title)
        ? "W"
        : StringInfo.GetNextTextElement(Title).ToUpperInvariant();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ScheduleSearch(value);
            }
        }
    }

    public string SearchResultText
    {
        get => _searchResultText;
        private set
        {
            if (SetProperty(ref _searchResultText, value))
            {
                OnPropertyChanged(nameof(HasSearchResultText));
            }
        }
    }

    public string? SelectedIdentity
    {
        get => _selectedIdentity;
        set
        {
            if (SetProperty(ref _selectedIdentity, value) && _session is not null)
            {
                if (_selectedChat is not null)
                {
                    _selectedChat.SelectedIdentity = value;
                }

                RebuildTimeline();
            }
        }
    }

    public DateTimeOffset? JumpDate
    {
        get => _jumpDate;
        set => SetProperty(ref _jumpDate, value);
    }

    public TimeSpan? JumpTime
    {
        get => _jumpTime;
        set => SetProperty(ref _jumpTime, value);
    }

    public ChatDisplayItemViewModel? SelectedTimelineItem
    {
        get => _selectedTimelineItem;
        set => SetProperty(ref _selectedTimelineItem, value);
    }

    public Task LoadArchiveAsync(string archivePath) =>
        LoadArchivesAsync([archivePath], replaceExisting: true);

    public async Task LoadArchivesAsync(
        IEnumerable<string> archivePaths,
        bool replaceExisting = false)
    {
        var paths = archivePaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0)
        {
            return;
        }

        if (!replaceExisting)
        {
            var existingPaths = Chats
                .Select(static chat => chat.Session.ArchivePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var firstExisting = Chats.FirstOrDefault(chat =>
                paths.Contains(chat.Session.ArchivePath, StringComparer.OrdinalIgnoreCase));
            paths = paths.Where(path => !existingPaths.Contains(path)).ToArray();
            if (paths.Length == 0)
            {
                if (firstExisting is not null)
                {
                    SelectedChat = firstExisting;
                }

                return;
            }
        }

        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var cancellationToken = _loadCancellation.Token;
        var generation = ++_loadGeneration;
        var loadedChats = new List<ChatListItemViewModel>();
        var failures = new List<string>();

        IsBusy = true;
        ErrorText = string.Empty;

        try
        {
            for (var index = 0; index < paths.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = paths[index];
                var archiveLabel = Path.GetFileName(path);
                StatusText = paths.Length == 1
                    ? "Opening ZIP directory…"
                    : $"Opening {index + 1} of {paths.Length}: {archiveLabel}";
                var currentIndex = index;
                var progress = new Progress<LoadProgress>(update =>
                {
                    if (generation != _loadGeneration)
                    {
                        return;
                    }

                    var stage = update.MessageCount > 0
                        ? $"{update.Stage}  {update.MessageCount:N0}"
                        : update.Stage;
                    StatusText = paths.Length == 1
                        ? stage
                        : $"{currentIndex + 1} of {paths.Length} · {archiveLabel} · {stage}";
                });

                ChatSession? session = null;
                try
                {
                    session = await Task.Run(
                        () => _loader.Load(path, progress, cancellationToken),
                        cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    loadedChats.Add(new ChatListItemViewModel(session));
                    session = null;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (exception is
                    IOException or
                    InvalidDataException or
                    UnauthorizedAccessException)
                {
                    failures.Add($"{archiveLabel}: {exception.Message}");
                }
                finally
                {
                    session?.Dispose();
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (replaceExisting && loadedChats.Count > 0)
            {
                DisposeChats();
                Chats.Clear();
                _selectedChat = null;
                _session = null;
            }

            foreach (var chat in loadedChats)
            {
                Chats.Add(chat);
            }

            var loadedCount = loadedChats.Count;
            var firstLoaded = loadedChats.FirstOrDefault();
            loadedChats.Clear();
            HasChat = Chats.Count > 0;
            if (firstLoaded is not null)
            {
                SelectedChat = firstLoaded;
            }

            ErrorText = failures.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine, failures);
            StatusText = firstLoaded is null
                ? "Could not open the selected export"
                : failures.Count == 0
                    ? $"Ready · {Chats.Count:N0} chat{(Chats.Count == 1 ? string.Empty : "s")} open"
                    : $"Ready · {loadedCount:N0} opened, {failures.Count:N0} skipped";
        }
        catch (OperationCanceledException)
        {
            if (generation == _loadGeneration)
            {
                StatusText = "Loading cancelled";
            }
        }
        finally
        {
            foreach (var chat in loadedChats)
            {
                chat.Session.Dispose();
            }

            if (generation == _loadGeneration)
            {
                IsBusy = false;
            }
        }
    }

    public void ScrollToBottom()
    {
        var target = Items.LastOrDefault(static item => item.IsMessage || item.IsSystem);
        if (target is not null)
        {
            SelectedTimelineItem = target;
            ScrollRequested?.Invoke(target);
        }
    }

    public void DismissError() => ErrorText = string.Empty;

    public void Dispose()
    {
        _loadCancellation?.Cancel();
        _searchCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _searchCancellation?.Dispose();
        DisposeChats();
        Chats.Clear();
        _session = null;
    }

    private void ActivateChat(ChatListItemViewModel? chat)
    {
        if (chat is null)
        {
            _session = null;
            _mediaGallery = [];
            _allItems = [];
            _messageItems = [];
            Items = [];
            HasChat = false;
            return;
        }

        _session = chat.Session;
        _mediaGallery = chat.MediaGallery;
        Title = chat.Title;
        Subtitle = "online in your archive";
        ArchiveName = chat.ArchiveName;
        ArchiveDetails = chat.ArchiveDetails;
        MessageCountText = chat.MessageCountText;
        MediaCountText = chat.MediaCountText;
        RangeText = chat.RangeText;
        OnPropertyChanged(nameof(ContactInitial));
        OnPropertyChanged(nameof(Media));
        OnPropertyChanged(nameof(MediaGallery));

        _searchCancellation?.Cancel();
        _searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
        SearchResultText = string.Empty;
        SelectedTimelineItem = null;

        Participants.Clear();
        foreach (var participant in _session.Participants)
        {
            Participants.Add(participant);
        }

        chat.SelectedIdentity ??= GuessIdentity(_session);
        _selectedIdentity = chat.SelectedIdentity;
        OnPropertyChanged(nameof(SelectedIdentity));

        if (_session.Messages.Count > 0)
        {
            var last = _session.Messages[^1].Timestamp;
            JumpDate = new DateTimeOffset(last.Date);
            JumpTime = last.TimeOfDay;
        }

        HasChat = true;
        RebuildTimeline();
        TimelineReady?.Invoke();
    }

    private void RebuildTimeline()
    {
        if (_session is null)
        {
            return;
        }

        var isGroup = _session.Participants.Count > 2 ||
                      !_session.Participants.Any(participant =>
                          participant.Equals(_session.Title, StringComparison.OrdinalIgnoreCase));
        var items = new List<ChatDisplayItemViewModel>(_session.Messages.Count + 512);
        var messageItems = new ChatDisplayItemViewModel[_session.Messages.Count];
        DateTime? day = null;

        for (var index = 0; index < _session.Messages.Count; index++)
        {
            var message = _session.Messages[index];
            if (day != message.Timestamp.Date)
            {
                day = message.Timestamp.Date;
                items.Add(ChatDisplayItemViewModel.Date(day.Value));
            }

            ChatDisplayItemViewModel item;
            if (message.IsSystem)
            {
                item = ChatDisplayItemViewModel.ForSystem(message);
            }
            else
            {
                var connectedToPrevious = index > 0 && IsSameBubbleGroup(_session.Messages[index - 1], message);
                var connectedToNext = index + 1 < _session.Messages.Count && IsSameBubbleGroup(message, _session.Messages[index + 1]);
                item = ChatDisplayItemViewModel.ForMessage(
                    message,
                    string.Equals(message.Sender, SelectedIdentity, StringComparison.OrdinalIgnoreCase),
                    isGroup,
                    connectedToPrevious,
                    connectedToNext,
                    _session.Media,
                    request => AttachmentOpenRequested?.Invoke(request));
            }

            items.Add(item);
            messageItems[message.Index] = item;
        }

        _allItems = items;
        _messageItems = messageItems;
        ApplySearchImmediately(SearchText);
    }

    private static bool IsSameBubbleGroup(ChatMessage first, ChatMessage second) =>
        !first.IsSystem &&
        !second.IsSystem &&
        first.Timestamp.Date == second.Timestamp.Date &&
        second.Timestamp - first.Timestamp <= TimeSpan.FromMinutes(2) &&
        string.Equals(first.Sender, second.Sender, StringComparison.OrdinalIgnoreCase);

    private void JumpToSelectedTime()
    {
        if (_session is null || _session.Messages.Count == 0 || JumpDate is null)
        {
            return;
        }

        var time = JumpTime ?? TimeSpan.Zero;
        var targetTime = JumpDate.Value.Date + time;
        var index = TimestampSearch.FindFirstAtOrAfter(_session.Messages, targetTime);
        if (index < 0)
        {
            return;
        }

        ClearSearchImmediately();
        var target = _messageItems[index];
        SelectedTimelineItem = target;
        target.IsHighlighted = true;
        var generation = ++_highlightGeneration;
        ScrollRequested?.Invoke(target);
        _ = ClearHighlightLaterAsync(target, generation);
        StatusText = $"Jumped to {target.Message?.Timestamp:ddd, MMM d yyyy · HH:mm}";
    }

    private async Task ClearHighlightLaterAsync(ChatDisplayItemViewModel item, int generation)
    {
        await Task.Delay(TimeSpan.FromSeconds(2.5));
        if (generation == _highlightGeneration)
        {
            await Dispatcher.UIThread.InvokeAsync(() => item.IsHighlighted = false);
        }
    }

    private void ScheduleSearch(string query)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        _ = ApplySearchAfterDelayAsync(query, _searchCancellation.Token);
    }

    private async Task ApplySearchAfterDelayAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(180, cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() => ApplySearchImmediately(query));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ApplySearchImmediately(string query)
    {
        if (_session is null || string.IsNullOrWhiteSpace(query))
        {
            Items = _allItems;
            SearchResultText = string.Empty;
            return;
        }

        var needle = query.Trim();
        var results = new List<ChatDisplayItemViewModel>();
        DateTime? day = null;
        foreach (var message in _session.Messages)
        {
            if (!Matches(message, needle))
            {
                continue;
            }

            if (day != message.Timestamp.Date)
            {
                day = message.Timestamp.Date;
                results.Add(ChatDisplayItemViewModel.Date(day.Value));
            }

            results.Add(_messageItems[message.Index]);
        }

        Items = results;
        var matchCount = results.Count(static item => item.IsMessage || item.IsSystem);
        SearchResultText = $"{matchCount:N0} match{(matchCount == 1 ? string.Empty : "es")}";
    }

    private void ClearSearchImmediately()
    {
        _searchCancellation?.Cancel();
        _searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
        Items = _allItems;
        SearchResultText = string.Empty;
    }

    private void DisposeChats()
    {
        foreach (var chat in Chats)
        {
            chat.Session.Dispose();
        }
    }

    private static bool Matches(ChatMessage message, string needle) =>
        message.Text.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ||
        (message.Sender?.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
        (message.ReferencedAttachmentName?.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ?? false);

    private static string? GuessIdentity(ChatSession session)
    {
        var otherParticipant = session.Participants.FirstOrDefault(participant =>
            !participant.Equals(session.Title, StringComparison.OrdinalIgnoreCase));
        return otherParticipant ?? session.Participants.FirstOrDefault();
    }
}
