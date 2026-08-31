using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace WhatsAppChatViewer.Controls;

public sealed partial class VideoPlayerOverlay : UserControl
{
    private LazyArchiveVideoPlayer? _owner;
    private readonly Button _centerPlayButton;
    private readonly Button _previousMediaButton;
    private readonly Button _nextMediaButton;
    private readonly Border _loadingPanel;
    private readonly PathIcon _playIcon;
    private readonly PathIcon _pauseIcon;
    private readonly PathIcon _volumeIcon;
    private readonly PathIcon _mutedIcon;
    private readonly PathIcon _fullscreenIcon;
    private readonly PathIcon _fullscreenExitIcon;
    private readonly Button _rotateLeftButton;
    private readonly Button _rotateRightButton;
    private readonly TextBlock _timeText;
    private readonly Slider _timelineSlider;
    private readonly Slider _volumeSlider;
    private bool _updatingTimeline;
    private bool _updatingVolume;

    public VideoPlayerOverlay()
    {
        AvaloniaXamlLoader.Load(this);

        _centerPlayButton = Require<Button>("CenterPlayButton");
        _previousMediaButton = Require<Button>("PreviousMediaButton");
        _nextMediaButton = Require<Button>("NextMediaButton");
        _loadingPanel = Require<Border>("LoadingPanel");
        _playIcon = Require<PathIcon>("PlayIcon");
        _pauseIcon = Require<PathIcon>("PauseIcon");
        _volumeIcon = Require<PathIcon>("VolumeIcon");
        _mutedIcon = Require<PathIcon>("MutedIcon");
        _fullscreenIcon = Require<PathIcon>("FullscreenIcon");
        _fullscreenExitIcon = Require<PathIcon>("FullscreenExitIcon");
        _rotateLeftButton = Require<Button>("RotateLeftButton");
        _rotateRightButton = Require<Button>("RotateRightButton");
        _timeText = Require<TextBlock>("TimeText");
        _timelineSlider = Require<Slider>("TimelineSlider");
        _volumeSlider = Require<Slider>("VolumeSlider");

        _timelineSlider.PropertyChanged += (_, change) =>
        {
            if (!_updatingTimeline && change.Property == RangeBase.ValueProperty)
            {
                _owner?.SeekNormalized(_timelineSlider.Value);
            }
        };
        _volumeSlider.PropertyChanged += (_, change) =>
        {
            if (!_updatingVolume && change.Property == RangeBase.ValueProperty)
            {
                _owner?.SetVolume(_volumeSlider.Value);
            }
        };
    }

    public VideoPlayerOverlay(LazyArchiveVideoPlayer owner)
        : this()
    {
        _owner = owner;
    }

    public void SetLoading(bool loading)
    {
        _loadingPanel.IsVisible = loading;
        _rotateLeftButton.IsEnabled = !loading;
        _rotateRightButton.IsEnabled = !loading;
        if (loading)
        {
            _centerPlayButton.IsVisible = false;
        }
    }

    public void SetPlaying(bool playing)
    {
        _playIcon.IsVisible = !playing;
        _pauseIcon.IsVisible = playing;
        _centerPlayButton.IsVisible = !playing && !_loadingPanel.IsVisible;
    }

    public void SetProgress(long currentMilliseconds, long durationMilliseconds)
    {
        _updatingTimeline = true;
        _timelineSlider.Value = durationMilliseconds > 0
            ? Math.Clamp((double)currentMilliseconds / durationMilliseconds, 0, 1)
            : 0;
        _updatingTimeline = false;
        _timeText.Text = $"{FormatTime(currentMilliseconds)} / {FormatTime(durationMilliseconds)}";
    }

    public void SetMuted(bool muted, int volume)
    {
        _volumeIcon.IsVisible = !muted;
        _mutedIcon.IsVisible = muted;
        _updatingVolume = true;
        _volumeSlider.Value = volume;
        _updatingVolume = false;
    }

    public void SetFullscreen(bool fullscreen)
    {
        _fullscreenIcon.IsVisible = !fullscreen;
        _fullscreenExitIcon.IsVisible = fullscreen;
    }

    public void SetNavigation(bool hasPrevious, bool hasNext)
    {
        _previousMediaButton.IsVisible = hasPrevious;
        _nextMediaButton.IsVisible = hasNext;
    }

    public void SetRotation(int degrees)
    {
        var normalized = ((degrees % 360) + 360) % 360;
        ToolTip.SetTip(
            _rotateLeftButton,
            normalized == 0
                ? "Rotate left (Shift+R)"
                : $"Current rotation: {normalized} degrees. Rotate left (Shift+R)");
        ToolTip.SetTip(
            _rotateRightButton,
            normalized == 0
                ? "Rotate right (R)"
                : $"Current rotation: {normalized} degrees. Rotate right (R)");
    }

    private void PlayPause_Click(object? sender, RoutedEventArgs e) => _owner?.TogglePlayPause();

    private void Mute_Click(object? sender, RoutedEventArgs e) => _owner?.ToggleMute();

    private void RotateLeft_Click(object? sender, RoutedEventArgs e) => _owner?.RotateCounterclockwise();

    private void RotateRight_Click(object? sender, RoutedEventArgs e) => _owner?.RotateClockwise();

    private void PreviousMedia_Click(object? sender, RoutedEventArgs e) => _owner?.RequestPrevious();

    private void NextMedia_Click(object? sender, RoutedEventArgs e) => _owner?.RequestNext();

    private void Fullscreen_Click(object? sender, RoutedEventArgs e) => _owner?.RequestFullscreen();

    private void Overlay_PointerPressed(object? sender, PointerPressedEventArgs e) => Focus();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space:
                _owner?.TogglePlayPause();
                e.Handled = true;
                break;
            case Key.Left:
                _owner?.SeekRelative(TimeSpan.FromSeconds(-5));
                e.Handled = true;
                break;
            case Key.Right:
                _owner?.SeekRelative(TimeSpan.FromSeconds(5));
                e.Handled = true;
                break;
            case Key.M:
                _owner?.ToggleMute();
                e.Handled = true;
                break;
            case Key.R:
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    _owner?.RotateCounterclockwise();
                }
                else
                {
                    _owner?.RotateClockwise();
                }

                e.Handled = true;
                break;
            case Key.F:
            case Key.F11:
                _owner?.RequestFullscreen();
                e.Handled = true;
                break;
        }

        base.OnKeyDown(e);
    }

    private T Require<T>(string name) where T : Control =>
        this.FindControl<T>(name) ?? throw new InvalidOperationException($"{name} was not created.");

    private static string FormatTime(long milliseconds)
    {
        var duration = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";
    }
}
