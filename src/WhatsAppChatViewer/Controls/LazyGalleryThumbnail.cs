using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using WhatsAppChatViewer.Services;
using WhatsAppChatViewer.ViewModels;

namespace WhatsAppChatViewer.Controls;

public sealed class LazyGalleryThumbnail : Control
{
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.Parse("#18252C"));
    private static readonly IBrush LoadingBrush = new SolidColorBrush(Color.Parse("#0A8ED9"));
    private static readonly IBrush VideoOverlayBrush = new SolidColorBrush(Color.Parse("#330B141A"));
    private static readonly IBrush VideoBadgeBrush = new SolidColorBrush(Color.Parse("#D92A3942"));
    private static readonly IBrush VideoFooterBrush = new SolidColorBrush(Color.Parse("#B3000000"));
    private static readonly IBrush ForegroundBrush = new SolidColorBrush(Color.Parse("#E9EDEF"));
    private static readonly Pen UnavailablePen = new(
        new SolidColorBrush(Color.Parse("#667781")),
        1.5,
        lineCap: PenLineCap.Round,
        lineJoin: PenLineJoin.Round);
    private static readonly StreamGeometry PlayGeometry = StreamGeometry.Parse(
        "M36,22 L36,40 L50,31 Z");
    private static readonly FormattedText VideoLabel = new(
        "VIDEO",
        CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight,
        new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold),
        9,
        ForegroundBrush);

    private CancellationTokenSource? _loadCancellation;
    private GalleryThumbnailLease? _thumbnailLease;
    private bool _attached;
    private bool _isLoading;
    private bool _isUnavailable;
    private bool _isVideo;
    private int _generation;

    public LazyGalleryThumbnail()
    {
        Width = 82;
        Height = 62;
        ClipToBounds = true;

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

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        context.DrawRectangle(BackgroundBrush, null, bounds, 5, 5);

        using (context.PushClip(new RoundedRect(bounds, 5)))
        {
            if (_thumbnailLease is { } lease)
            {
                context.DrawImage(lease.Bitmap, GetUniformFillSource(lease.Bitmap.Size, bounds.Size), bounds);
            }

            if (_isVideo)
            {
                DrawVideoMarker(context, bounds);
            }
            else if (_isLoading)
            {
                context.DrawRectangle(
                    LoadingBrush,
                    null,
                    new Rect((bounds.Width - 42) / 2, (bounds.Height - 3) / 2, 42, 3),
                    1.5,
                    1.5);
            }
            else if (_isUnavailable)
            {
                DrawUnavailableMarker(context, bounds);
            }
        }
    }

    private async void RestartLoad()
    {
        if (!_attached)
        {
            return;
        }

        CancelAndRelease();
        if (DataContext is not MediaGalleryItemViewModel item)
        {
            ToolTip.SetTip(this, null);
            return;
        }

        var generation = ++_generation;
        _loadCancellation = new CancellationTokenSource();
        _isLoading = true;
        _isUnavailable = false;
        _isVideo = item.IsVideo;
        ToolTip.SetTip(this, item.ToolTipText);
        InvalidateVisual();

        try
        {
            var lease = await item.Archive.AcquireGalleryThumbnailAsync(
                item.Attachment,
                180,
                _loadCancellation.Token);
            if (!_attached || generation != _generation || _loadCancellation.IsCancellationRequested)
            {
                lease.Dispose();
                return;
            }

            _thumbnailLease = lease;
            _isLoading = false;
            InvalidateVisual();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (generation == _generation)
            {
                _isLoading = false;
                _isUnavailable = true;
                InvalidateVisual();
            }
        }
    }

    private void CancelAndRelease()
    {
        _generation++;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        _thumbnailLease?.Dispose();
        _thumbnailLease = null;
        _isLoading = false;
        _isUnavailable = false;
        _isVideo = false;
        InvalidateVisual();
    }

    private static Rect GetUniformFillSource(Size source, Size destination)
    {
        if (source.Width <= 0 || source.Height <= 0 || destination.Width <= 0 || destination.Height <= 0)
        {
            return new Rect(source);
        }

        var sourceAspect = source.Width / source.Height;
        var destinationAspect = destination.Width / destination.Height;
        if (sourceAspect > destinationAspect)
        {
            var width = source.Height * destinationAspect;
            return new Rect((source.Width - width) / 2, 0, width, source.Height);
        }

        var height = source.Width / destinationAspect;
        return new Rect(0, (source.Height - height) / 2, source.Width, height);
    }

    private static void DrawVideoMarker(DrawingContext context, Rect bounds)
    {
        context.DrawRectangle(VideoOverlayBrush, null, bounds);
        context.DrawEllipse(VideoBadgeBrush, null, new Rect(24, 14, 34, 34));
        context.DrawGeometry(ForegroundBrush, null, PlayGeometry);
        context.DrawRectangle(VideoFooterBrush, null, new Rect(0, bounds.Height - 17, bounds.Width, 17));
        context.DrawText(
            VideoLabel,
            new Point((bounds.Width - VideoLabel.Width) / 2, bounds.Height - VideoLabel.Height - 2));
    }

    private static void DrawUnavailableMarker(DrawingContext context, Rect bounds)
    {
        var marker = new Rect((bounds.Width - 22) / 2, (bounds.Height - 18) / 2, 22, 18);
        context.DrawRectangle(null, UnavailablePen, marker, 2, 2);
        context.DrawLine(
            UnavailablePen,
            new Point(marker.Left + 3, marker.Bottom - 3),
            new Point(marker.Right - 3, marker.Top + 3));
    }
}
