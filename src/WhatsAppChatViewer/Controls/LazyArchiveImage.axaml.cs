using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using WhatsAppChatViewer.ViewModels;

namespace WhatsAppChatViewer.Controls;

public sealed partial class LazyArchiveImage : UserControl
{
    private readonly Image _previewImage;
    private readonly Grid _loadingPanel;
    private readonly Border _errorPanel;
    private readonly TextBlock _errorText;
    private CancellationTokenSource? _loadCancellation;
    private Bitmap? _bitmap;
    private bool _attached;
    private int _loadGeneration;

    public LazyArchiveImage()
    {
        AvaloniaXamlLoader.Load(this);
        _previewImage = this.FindControl<Image>("PreviewImage")
            ?? throw new InvalidOperationException("PreviewImage was not created.");
        _loadingPanel = this.FindControl<Grid>("LoadingPanel")
            ?? throw new InvalidOperationException("LoadingPanel was not created.");
        _errorPanel = this.FindControl<Border>("ErrorPanel")
            ?? throw new InvalidOperationException("ErrorPanel was not created.");
        _errorText = this.FindControl<TextBlock>("ErrorText")
            ?? throw new InvalidOperationException("ErrorText was not created.");

        AttachedToVisualTree += (_, _) =>
        {
            _attached = true;
            RestartLoad();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _attached = false;
            CancelAndRelease();
        };
        DataContextChanged += (_, _) => RestartLoad();
    }

    private async void RestartLoad()
    {
        if (!_attached)
        {
            return;
        }

        CancelAndRelease();
        if (DataContext is not ChatDisplayItemViewModel { IsImage: true, Attachment: not null, Archive: not null } item)
        {
            return;
        }

        var generation = ++_loadGeneration;
        _loadCancellation = new CancellationTokenSource();
        _loadingPanel.IsVisible = true;
        _errorPanel.IsVisible = false;

        try
        {
            var bitmap = await item.Archive.LoadBitmapAsync(item.Attachment, 560, _loadCancellation.Token);
            if (!_attached || generation != _loadGeneration || _loadCancellation.IsCancellationRequested)
            {
                bitmap.Dispose();
                return;
            }

            _bitmap = bitmap;
            _previewImage.Source = bitmap;
            _loadingPanel.IsVisible = false;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (generation == _loadGeneration)
            {
                _loadingPanel.IsVisible = false;
                _errorPanel.IsVisible = true;
                _errorText.Text = exception is InvalidDataException ? "Preview too large" : "Preview unavailable";
            }
        }
    }

    private void CancelAndRelease()
    {
        _loadGeneration++;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        _previewImage.Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
    }

    private void OpenImage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ChatDisplayItemViewModel item && item.OpenAttachmentCommand.CanExecute(null))
        {
            item.OpenAttachmentCommand.Execute(null);
        }
    }
}
