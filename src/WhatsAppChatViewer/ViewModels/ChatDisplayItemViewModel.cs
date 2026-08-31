using System.Windows.Input;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using WhatsAppChatViewer.Models;
using WhatsAppChatViewer.Services;

namespace WhatsAppChatViewer.ViewModels;

public sealed class ChatDisplayItemViewModel : ObservableObject
{
    private static readonly IBrush IncomingBrush = new SolidColorBrush(Color.Parse("#202C33"));
    private static readonly IBrush OutgoingBrush = new SolidColorBrush(Color.Parse("#005C4B"));
    private static readonly IBrush TransparentBrush = Brushes.Transparent;
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.Parse("#00A884"));
    private bool _isHighlighted;
    private Action<AttachmentOpenRequest>? _attachmentOpener;

    private ChatDisplayItemViewModel()
    {
        OpenAttachmentCommand = new RelayCommand(() => { });
        OpenAttachmentFullscreenCommand = new RelayCommand(() => { });
    }

    public bool IsDateSeparator { get; private init; }
    public bool IsSystem { get; private init; }
    public bool IsMessage { get; private init; }
    public string DateLabel { get; private init; } = string.Empty;
    public DateTime? TimelineDate { get; private init; }
    public string PersistentDateLabel => TimelineDate is { } date ? FormatDate(date) : string.Empty;
    public string SystemText { get; private init; } = string.Empty;
    public ChatMessage? Message { get; private init; }
    public ArchiveMediaService? Archive { get; private init; }
    public ArchiveAttachment? Attachment => Message?.Attachment;
    public bool IsMine { get; private init; }
    public bool ShowIncomingTail { get; private init; }
    public bool ShowOutgoingTail { get; private init; }
    public HorizontalAlignment BubbleAlignment => IsMine ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    public IBrush BubbleBrush => IsMine ? OutgoingBrush : IncomingBrush;
    public CornerRadius BubbleCornerRadius { get; private init; } = new(9);
    public Thickness ItemMargin { get; private init; } = new(0, 5, 0, 0);
    public string SenderText => Message?.Sender ?? string.Empty;
    public bool ShowSender { get; private init; }
    public string MessageText => Message?.Text ?? string.Empty;
    public bool HasText => !string.IsNullOrWhiteSpace(MessageText);
    public string TimeText => Message?.Timestamp.ToString("HH:mm") ?? string.Empty;
    public string DeliveryText => IsMine ? "✓✓" : string.Empty;
    public bool IsEdited => Message?.IsEdited == true;
    public bool HasResolvedAttachment => Attachment is not null;
    public bool HasMissingAttachment => Message?.ReferencedAttachmentName is not null && Attachment is null;
    public bool IsImage => Attachment?.Kind is AttachmentKind.Image or AttachmentKind.Sticker or AttachmentKind.AnimatedImage;
    public bool IsVideo => Attachment?.Kind == AttachmentKind.Video;
    public bool IsSticker => Attachment?.Kind == AttachmentKind.Sticker;
    public bool IsFileCard => HasResolvedAttachment && !IsImage && !IsVideo;
    public double ImageHeight => IsSticker ? 210 : 300;
    public string AttachmentName => Attachment?.FileName ?? Message?.ReferencedAttachmentName ?? "Media omitted";
    public string AttachmentSize => Attachment?.SizeText ?? string.Empty;
    public string AttachmentIcon => Attachment?.Kind switch
    {
        AttachmentKind.Video => "▶",
        AttachmentKind.Audio => "♪",
        AttachmentKind.Document => "▤",
        AttachmentKind.Contact => "♙",
        _ => "↓"
    };

    public string AttachmentTypeText => Attachment?.Kind switch
    {
        AttachmentKind.Video => "VIDEO",
        AttachmentKind.Audio => "VOICE MESSAGE",
        AttachmentKind.Document => Path.GetExtension(Attachment.FileName).TrimStart('.').ToUpperInvariant(),
        AttachmentKind.Contact => "CONTACT",
        _ => "ATTACHMENT"
    };

    public ICommand OpenAttachmentCommand { get; private init; }
    public ICommand OpenAttachmentFullscreenCommand { get; private init; }
    public IBrush HighlightBorderBrush => _isHighlighted ? HighlightBrush : TransparentBrush;
    public double HighlightBorderThickness => _isHighlighted ? 2 : 0;

    public bool IsHighlighted
    {
        get => _isHighlighted;
        set
        {
            if (SetProperty(ref _isHighlighted, value))
            {
                OnPropertyChanged(nameof(HighlightBorderBrush));
                OnPropertyChanged(nameof(HighlightBorderThickness));
            }
        }
    }

    public void OpenVideoFullscreen(VideoPlaybackState playbackState)
    {
        if (Attachment is not null)
        {
            _attachmentOpener?.Invoke(new AttachmentOpenRequest(Attachment, true, playbackState));
        }
    }

    public static ChatDisplayItemViewModel Date(DateTime date) => new()
    {
        IsDateSeparator = true,
        DateLabel = FormatDate(date),
        TimelineDate = date
    };

    public static ChatDisplayItemViewModel ForSystem(ChatMessage message) => new()
    {
        IsSystem = true,
        SystemText = message.Text,
        Message = message,
        TimelineDate = message.Timestamp.Date
    };

    public static ChatDisplayItemViewModel ForMessage(
        ChatMessage message,
        bool isMine,
        bool showSender,
        bool connectedToPrevious,
        bool connectedToNext,
        ArchiveMediaService archive,
        Action<AttachmentOpenRequest> openAttachment) => new()
        {
            IsMessage = true,
            Message = message,
            TimelineDate = message.Timestamp.Date,
            IsMine = isMine,
            ShowSender = showSender && !isMine && !connectedToPrevious,
            ShowIncomingTail = !isMine && !connectedToPrevious,
            ShowOutgoingTail = isMine && !connectedToPrevious,
            BubbleCornerRadius = GetCornerRadius(isMine, connectedToPrevious, connectedToNext),
            ItemMargin = new Thickness(0, connectedToPrevious ? 1 : 6, 0, 0),
            Archive = archive,
            _attachmentOpener = openAttachment,
            OpenAttachmentCommand = new RelayCommand(() =>
            {
                if (message.Attachment is not null)
                {
                    openAttachment(new AttachmentOpenRequest(message.Attachment));
                }
            }, () => message.Attachment is not null),
            OpenAttachmentFullscreenCommand = new RelayCommand(() =>
            {
                if (message.Attachment is not null)
                {
                    openAttachment(new AttachmentOpenRequest(message.Attachment, true));
                }
            }, () => message.Attachment is not null)
        };

    private static CornerRadius GetCornerRadius(bool isMine, bool connectedToPrevious, bool connectedToNext)
    {
        if (isMine)
        {
            return new CornerRadius(
                topLeft: 9,
                topRight: connectedToPrevious ? 4 : 3,
                bottomRight: connectedToNext ? 4 : 9,
                bottomLeft: 9);
        }

        return new CornerRadius(
            topLeft: connectedToPrevious ? 4 : 3,
            topRight: 9,
            bottomRight: 9,
            bottomLeft: connectedToNext ? 4 : 9);
    }

    private static string FormatDate(DateTime date)
    {
        var today = DateTime.Today;
        if (date.Date == today)
        {
            return "TODAY";
        }

        if (date.Date == today.AddDays(-1))
        {
            return "YESTERDAY";
        }

        return date.ToString("dddd, MMMM d, yyyy").ToUpperInvariant();
    }
}
