using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LibVLCSharp.Avalonia;
using LibVLCSharp.Shared;
using WhatsAppChatViewer.Models;
using WhatsAppChatViewer.Services;
using WhatsAppChatViewer.ViewModels;

namespace WhatsAppChatViewer.Controls;

public sealed partial class LazyArchiveVideoPlayer : UserControl, IDisposable
{
    private readonly ContentControl _videoHost;
    private readonly Grid _idlePanel;
    private readonly Grid _preparingPanel;
    private readonly Grid _errorPanel;
    private readonly TextBlock _idleTitle;
    private readonly TextBlock _idleDetails;
    private readonly TextBlock _preparingText;
    private readonly TextBlock _errorText;
    private ArchiveMediaService? _archive;
    private ArchiveAttachment? _attachment;
    private CancellationTokenSource? _prepareCancellation;
    private LibVLC? _libVlc;
    private MediaPlayer? _player;
    private Media? _media;
    private VideoView? _videoView;
    private VideoPlayerOverlay? _overlay;
    private VideoPlaybackState? _configuredStartState;
    private EventHandler<EventArgs>? _playingHandler;
    private EventHandler<EventArgs>? _pausedHandler;
    private EventHandler<EventArgs>? _stoppedHandler;
    private EventHandler<EventArgs>? _endReachedHandler;
    private EventHandler<MediaPlayerTimeChangedEventArgs>? _timeChangedHandler;
    private EventHandler<MediaPlayerLengthChangedEventArgs>? _lengthChangedHandler;
    private EventHandler<EventArgs>? _encounteredErrorHandler;
    private string? _sourcePath;
    private long? _resumeAfterStart;
    private bool _attached;
    private bool _autoPlay;
    private bool _explicitConfiguration;
    private bool _preparing;
    private bool _disposed;
    private bool _pauseAfterStart;
    private bool _hasEnded;
    private bool _isFullscreen;
    private bool _hasPrevious;
    private bool _hasNext;
    private int _rotationDegrees;
    private int _sessionVersion;

    public LazyArchiveVideoPlayer()
    {
        AvaloniaXamlLoader.Load(this);
        _videoHost = Require<ContentControl>("VideoHost");
        _idlePanel = Require<Grid>("IdlePanel");
        _preparingPanel = Require<Grid>("PreparingPanel");
        _errorPanel = Require<Grid>("ErrorPanel");
        _idleTitle = Require<TextBlock>("IdleTitle");
        _idleDetails = Require<TextBlock>("IdleDetails");
        _preparingText = Require<TextBlock>("PreparingText");
        _errorText = Require<TextBlock>("ErrorText");

        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
        DataContextChanged += (_, _) => ConfigureFromDataContext();
    }

    public event EventHandler? FullscreenRequested;
    public event EventHandler? PreviousRequested;
    public event EventHandler? NextRequested;

    public void Configure(
        ArchiveMediaService archive,
        ArchiveAttachment attachment,
        bool autoPlay = false,
        VideoPlaybackState? initialState = null)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(attachment);
        _explicitConfiguration = true;
        ConfigureCore(archive, attachment, autoPlay, initialState);
    }

    public void SetFullscreenState(bool fullscreen)
    {
        _isFullscreen = fullscreen;
        _overlay?.SetFullscreen(fullscreen);
    }

    public void SetNavigationState(bool hasPrevious, bool hasNext)
    {
        _hasPrevious = hasPrevious;
        _hasNext = hasNext;
        _overlay?.SetNavigation(hasPrevious, hasNext);
    }

    public void RequestPrevious()
    {
        if (_hasPrevious)
        {
            PreviousRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public void RequestNext()
    {
        if (_hasNext)
        {
            NextRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public void RefreshVideoSurface()
    {
        var videoView = _videoView;
        var player = _player;
        if (videoView is null || player is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_videoView, videoView) || !ReferenceEquals(_player, player))
            {
                return;
            }

            videoView.MediaPlayer = null;
            videoView.MediaPlayer = player;
        }, DispatcherPriority.Loaded);
    }

    public void TogglePlayPause()
    {
        if (_player is null)
        {
            _ = StartPlaybackAsync();
            return;
        }

        if (_player.IsPlaying)
        {
            _player.Pause();
        }
        else
        {
            if (HasPlaybackEnded(_player) ||
                (_player.Length > 0 && _player.Time >= _player.Length - 250))
            {
                RecreatePlayerAt(0, startPaused: false);
                return;
            }

            _player.Play();
        }
    }

    public void SeekNormalized(double position)
    {
        if (_player is null || _player.Length <= 0)
        {
            return;
        }

        var target = (long)Math.Round(Math.Clamp(position, 0, 1) * _player.Length);
        if (HasPlaybackEnded(_player) && target < _player.Length)
        {
            RecreatePlayerAt(target, startPaused: false);
            return;
        }

        _player.Time = target;
    }

    public void SeekRelative(TimeSpan delta)
    {
        if (_player is null || _player.Length <= 0)
        {
            return;
        }

        var target = Math.Clamp(
            _player.Time + (long)delta.TotalMilliseconds,
            0,
            _player.Length);
        if (HasPlaybackEnded(_player) && target < _player.Length)
        {
            RecreatePlayerAt(target, startPaused: false);
            return;
        }

        _player.Time = target;
    }

    public void SetVolume(double volume)
    {
        if (_player is null)
        {
            return;
        }

        _player.Volume = (int)Math.Round(Math.Clamp(volume, 0, 100));
        if (_player.Volume > 0 && _player.Mute)
        {
            _player.Mute = false;
        }

        _overlay?.SetMuted(_player.Mute, _player.Volume);
    }

    public void ToggleMute()
    {
        if (_player is null)
        {
            return;
        }

        _player.Mute = !_player.Mute;
        _overlay?.SetMuted(_player.Mute, _player.Volume);
    }

    public void RotateClockwise()
        => RotateBy(90);

    public void RotateCounterclockwise()
        => RotateBy(-90);

    public void StopPlayback()
    {
        if (_disposed)
        {
            return;
        }

        CancelPreparation();
        DisposePlayer();
        ShowIdle();
    }

    private void RotateBy(int deltaDegrees)
    {
        if (_disposed || _preparing || _player is null || string.IsNullOrWhiteSpace(_sourcePath))
        {
            return;
        }

        var path = _sourcePath;
        var currentTime = Math.Max(0, _player.Time);
        if (HasPlaybackEnded(_player) ||
            (_player.Length > 0 && currentTime >= _player.Length - 250))
        {
            currentTime = 0;
        }

        var resumePaused = !_player.IsPlaying;
        var knownDuration = Math.Max(0, _player.Length);
        var volume = _player.Volume;
        var muted = _player.Mute;
        _rotationDegrees = NormalizeRotation(_rotationDegrees + deltaDegrees);

        DisposePlayer();
        try
        {
            CreatePlayer(path, currentTime, resumePaused, volume, muted, knownDuration);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowError(exception.Message);
        }
    }

    public void RequestFullscreen()
    {
        if (!_explicitConfiguration && DataContext is ChatDisplayItemViewModel item)
        {
            var playbackState = _player is null
                ? new VideoPlaybackState(_rotationDegrees, 0, false, 80, false)
                : new VideoPlaybackState(
                    _rotationDegrees,
                    HasPlaybackEnded(_player) ? 0 : Math.Max(0, _player.Time),
                    !_player.IsPlaying,
                    _player.Volume,
                    _player.Mute);
            DisposePlayer();
            ShowIdle();
            item.OpenVideoFullscreen(playbackState);

            return;
        }

        FullscreenRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelPreparation();
        DisposePlayer();
    }

    private void OnAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _attached = true;
        ConfigureFromDataContext();
        if (_autoPlay && _player is null && !_preparing)
        {
            _ = StartPlaybackAsync();
        }
    }

    private void OnDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        CancelPreparation();
        DisposePlayer();
        ShowIdle();
    }

    private void ConfigureFromDataContext()
    {
        if (_explicitConfiguration ||
            DataContext is not ChatDisplayItemViewModel
            {
                IsVideo: true,
                Archive: not null,
                Attachment: not null
            } item)
        {
            return;
        }

        ConfigureCore(item.Archive, item.Attachment, autoPlay: false, initialState: null);
    }

    private void ConfigureCore(
        ArchiveMediaService archive,
        ArchiveAttachment attachment,
        bool autoPlay,
        VideoPlaybackState? initialState)
    {
        if (!ReferenceEquals(_archive, archive) ||
            !string.Equals(_attachment?.EntryName, attachment.EntryName, StringComparison.OrdinalIgnoreCase))
        {
            CancelPreparation();
            DisposePlayer();
            _sourcePath = null;
            _configuredStartState = initialState;
            _rotationDegrees = NormalizeRotation(initialState?.RotationDegrees ?? 0);
        }

        _archive = archive;
        _attachment = attachment;
        _autoPlay = autoPlay;
        _idleTitle.Text = attachment.FileName;
        _idleDetails.Text = $"VIDEO  ·  {attachment.SizeText}";
        ShowIdle();

        if (_attached && _autoPlay && _player is null && !_preparing)
        {
            _ = StartPlaybackAsync();
        }
    }

    private async Task StartPlaybackAsync()
    {
        if (_disposed || !_attached || _preparing || _player is not null ||
            _archive is null || _attachment is null)
        {
            return;
        }

        var archive = _archive;
        var attachment = _attachment;
        CancelPreparation();
        var sessionVersion = ++_sessionVersion;
        _preparing = true;
        _idlePanel.IsVisible = false;
        _errorPanel.IsVisible = false;
        _preparingPanel.IsVisible = true;
        _preparingText.Text = "Preparing video from ZIP…";

        _prepareCancellation = new CancellationTokenSource();
        var cancellationToken = _prepareCancellation.Token;
        var progress = new Progress<double>(value =>
            _preparingText.Text = $"Preparing video from ZIP… {value:P0}");

        try
        {
            var path = await archive.CreateTemporaryCopyAsync(attachment, progress, cancellationToken);
            if (!_attached || _disposed || sessionVersion != _sessionVersion ||
                !ReferenceEquals(archive, _archive) ||
                !string.Equals(attachment.EntryName, _attachment?.EntryName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var startState = _configuredStartState;
            CreatePlayer(
                path,
                startState?.PositionMilliseconds ?? 0,
                startState?.IsPaused ?? false,
                startState?.Volume ?? 80,
                startState?.IsMuted ?? false);
            _configuredStartState = null;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (_attached && sessionVersion == _sessionVersion)
            {
                ShowError(exception.Message);
            }
        }
        finally
        {
            if (sessionVersion == _sessionVersion)
            {
                _preparing = false;
                _preparingPanel.IsVisible = false;
            }
        }
    }

    private void CreatePlayer(
        string path,
        long resumeAtMilliseconds = 0,
        bool startPaused = false,
        int volume = 80,
        bool muted = false,
        long knownDurationMilliseconds = 0)
    {
        Core.Initialize();
        _sourcePath = path;
        _resumeAfterStart = resumeAtMilliseconds > 0 ? resumeAtMilliseconds : null;
        _pauseAfterStart = startPaused;
        _hasEnded = false;
        _libVlc = new LibVLC(GetLibVlcOptions(_rotationDegrees));
        _player = new MediaPlayer(_libVlc)
        {
            EnableKeyInput = false,
            EnableMouseInput = false,
            Volume = Math.Clamp(volume, 0, 100),
            Mute = muted
        };
        var player = _player;
        var playerSessionVersion = _sessionVersion;
        _playingHandler = (_, _) => Player_Playing(player, playerSessionVersion);
        _pausedHandler = (_, _) => Player_Paused(player, playerSessionVersion);
        _stoppedHandler = (_, _) => Player_Stopped(player, playerSessionVersion);
        _endReachedHandler = (_, _) => Player_EndReached(player, playerSessionVersion);
        _timeChangedHandler = (_, eventArgs) =>
            Player_TimeChanged(player, playerSessionVersion, eventArgs.Time);
        _lengthChangedHandler = (_, eventArgs) =>
            Player_LengthChanged(player, playerSessionVersion, eventArgs.Length);
        _encounteredErrorHandler = (_, _) => Player_EncounteredError(player, playerSessionVersion);
        player.Playing += _playingHandler;
        player.Paused += _pausedHandler;
        player.Stopped += _stoppedHandler;
        player.EndReached += _endReachedHandler;
        player.TimeChanged += _timeChangedHandler;
        player.LengthChanged += _lengthChangedHandler;
        player.EncounteredError += _encounteredErrorHandler;

        _overlay = new VideoPlayerOverlay(this);
        _overlay.SetLoading(true);
        _overlay.SetMuted(_player.Mute, _player.Volume);
        _overlay.SetRotation(_rotationDegrees);
        _overlay.SetFullscreen(_isFullscreen);
        _overlay.SetNavigation(_hasPrevious, _hasNext);
        if (resumeAtMilliseconds > 0 && knownDurationMilliseconds > 0)
        {
            _overlay.SetProgress(resumeAtMilliseconds, knownDurationMilliseconds);
        }
        _videoView = new VideoView
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Content = _overlay,
            MediaPlayer = _player
        };
        _videoHost.Content = _videoView;
        _media = new Media(_libVlc, new Uri(path));
        if (resumeAtMilliseconds > 0)
        {
            var startSeconds = (resumeAtMilliseconds / 1000d)
                .ToString("0.###", CultureInfo.InvariantCulture);
            _media.AddOption($":start-time={startSeconds}");
        }
        _idlePanel.IsVisible = false;
        _errorPanel.IsVisible = false;

        if (!_player.Play(_media))
        {
            throw new InvalidOperationException("The video player could not start this file.");
        }

        RefreshVideoSurface();
    }

    private void Player_Playing(MediaPlayer sourcePlayer, int playerSessionVersion)
    {
        PostToUi(sourcePlayer, playerSessionVersion, () =>
        {
            _hasEnded = false;
            if (_resumeAfterStart is { } resumeAt)
            {
                _ = EnsureStartPositionAsync(sourcePlayer, playerSessionVersion, resumeAt);
            }

            var pauseAfterStart = _pauseAfterStart;
            _pauseAfterStart = false;
            _overlay?.SetLoading(false);
            _overlay?.SetPlaying(!pauseAfterStart);
            if (pauseAfterStart)
            {
                sourcePlayer.SetPause(true);
            }
        });
    }

    private void Player_Paused(MediaPlayer sourcePlayer, int playerSessionVersion) =>
        PostToUi(sourcePlayer, playerSessionVersion, () => _overlay?.SetPlaying(false));

    private void Player_Stopped(MediaPlayer sourcePlayer, int playerSessionVersion)
    {
        PostToUi(sourcePlayer, playerSessionVersion, () =>
        {
            _overlay?.SetLoading(false);
            _overlay?.SetPlaying(false);
        });
    }

    private void Player_EndReached(MediaPlayer sourcePlayer, int playerSessionVersion)
    {
        PostToUi(sourcePlayer, playerSessionVersion, () =>
        {
            _hasEnded = true;
            _overlay?.SetLoading(false);
            _overlay?.SetPlaying(false);
            _overlay?.SetProgress(sourcePlayer.Length, sourcePlayer.Length);
        });
    }

    private void Player_TimeChanged(
        MediaPlayer sourcePlayer,
        int playerSessionVersion,
        long time) =>
        PostToUi(
            sourcePlayer,
            playerSessionVersion,
            () =>
            {
                if (_resumeAfterStart is { } resumeAt)
                {
                    if (time < resumeAt - 750)
                    {
                        return;
                    }

                    _resumeAfterStart = null;
                }

                if (sourcePlayer.Length <= 0 || time < sourcePlayer.Length - 250)
                {
                    _hasEnded = false;
                }

                _overlay?.SetProgress(time, sourcePlayer.Length);
            });

    private void Player_LengthChanged(
        MediaPlayer sourcePlayer,
        int playerSessionVersion,
        long length) =>
        PostToUi(
            sourcePlayer,
            playerSessionVersion,
            () => _overlay?.SetProgress(_resumeAfterStart ?? sourcePlayer.Time, length));

    private void Player_EncounteredError(MediaPlayer sourcePlayer, int playerSessionVersion) =>
        PostToUi(
            sourcePlayer,
            playerSessionVersion,
            () => ShowError("This video could not be decoded or played."));

    private void PostToUi(MediaPlayer sourcePlayer, int playerSessionVersion, Action action)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed &&
                _attached &&
                playerSessionVersion == _sessionVersion &&
                ReferenceEquals(sourcePlayer, _player))
            {
                action();
            }
        });
    }

    private void Start_Click(object? sender, RoutedEventArgs e) => _ = StartPlaybackAsync();

    private void Retry_Click(object? sender, RoutedEventArgs e)
    {
        DisposePlayer();
        _ = StartPlaybackAsync();
    }

    private void ShowIdle()
    {
        if (_preparing || _player is not null)
        {
            return;
        }

        _idlePanel.IsVisible = _attachment is not null;
        _preparingPanel.IsVisible = false;
        _errorPanel.IsVisible = false;
    }

    private void ShowError(string message)
    {
        DisposePlayer();
        _preparing = false;
        _preparingPanel.IsVisible = false;
        _idlePanel.IsVisible = false;
        _errorText.Text = string.IsNullOrWhiteSpace(message)
            ? "Video could not be played."
            : message;
        _errorPanel.IsVisible = true;
    }

    private void CancelPreparation()
    {
        _sessionVersion++;
        _prepareCancellation?.Cancel();
        _prepareCancellation?.Dispose();
        _prepareCancellation = null;
        _preparing = false;
    }

    private void DisposePlayer()
    {
        _sessionVersion++;
        _resumeAfterStart = null;
        _pauseAfterStart = false;
        _hasEnded = false;
        var player = _player;
        _player = null;

        if (_videoView is not null)
        {
            _videoView.MediaPlayer = null;
        }

        _videoHost.Content = null;
        _videoView = null;
        _overlay = null;

        if (player is not null)
        {
            if (_playingHandler is not null)
            {
                player.Playing -= _playingHandler;
            }

            if (_pausedHandler is not null)
            {
                player.Paused -= _pausedHandler;
            }

            if (_stoppedHandler is not null)
            {
                player.Stopped -= _stoppedHandler;
            }

            if (_endReachedHandler is not null)
            {
                player.EndReached -= _endReachedHandler;
            }

            if (_timeChangedHandler is not null)
            {
                player.TimeChanged -= _timeChangedHandler;
            }

            if (_lengthChangedHandler is not null)
            {
                player.LengthChanged -= _lengthChangedHandler;
            }

            if (_encounteredErrorHandler is not null)
            {
                player.EncounteredError -= _encounteredErrorHandler;
            }

            try
            {
                player.Stop();
            }
            catch
            {
                // Native playback may already be shutting down.
            }

            player.Dispose();
        }

        _playingHandler = null;
        _pausedHandler = null;
        _stoppedHandler = null;
        _endReachedHandler = null;
        _timeChangedHandler = null;
        _lengthChangedHandler = null;
        _encounteredErrorHandler = null;

        _media?.Dispose();
        _media = null;
        _libVlc?.Dispose();
        _libVlc = null;
    }

    private bool HasPlaybackEnded(MediaPlayer player) =>
        _hasEnded || player.State == VLCState.Ended;

    private void RecreatePlayerAt(long positionMilliseconds, bool startPaused)
    {
        if (_player is null || string.IsNullOrWhiteSpace(_sourcePath))
        {
            return;
        }

        var path = _sourcePath;
        var volume = _player.Volume;
        var muted = _player.Mute;
        var knownDuration = Math.Max(0, _player.Length);
        DisposePlayer();
        try
        {
            CreatePlayer(
                path,
                Math.Max(0, positionMilliseconds),
                startPaused,
                volume,
                muted,
                knownDuration);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowError(exception.Message);
        }
    }

    private async Task EnsureStartPositionAsync(
        MediaPlayer sourcePlayer,
        int playerSessionVersion,
        long requestedTime)
    {
        await Task.Delay(400);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_disposed ||
                !_attached ||
                playerSessionVersion != _sessionVersion ||
                !ReferenceEquals(sourcePlayer, _player) ||
                _resumeAfterStart != requestedTime ||
                sourcePlayer.Time >= requestedTime - 750)
            {
                return;
            }

            var maximum = sourcePlayer.Length > 0 ? sourcePlayer.Length : requestedTime;
            sourcePlayer.Time = Math.Clamp(requestedTime, 0, maximum);
        });
    }

    private static string[] GetLibVlcOptions(int rotationDegrees) => rotationDegrees switch
    {
        90 or 180 or 270 =>
        ["--no-video-title-show", "--quiet", "--video-filter=transform", $"--transform-type={rotationDegrees}"],
        _ => ["--no-video-title-show", "--quiet"]
    };

    private static int NormalizeRotation(int degrees) => ((degrees % 360) + 360) % 360;

    private T Require<T>(string name) where T : Control =>
        this.FindControl<T>(name) ?? throw new InvalidOperationException($"{name} was not created.");
}
