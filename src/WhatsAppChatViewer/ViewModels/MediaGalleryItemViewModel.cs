using System.Globalization;
using WhatsAppChatViewer.Models;
using WhatsAppChatViewer.Services;

namespace WhatsAppChatViewer.ViewModels;

public sealed class MediaGalleryItemViewModel : ObservableObject
{
    private bool _isCurrent;

    public MediaGalleryItemViewModel(
        ArchiveMediaService archive,
        ArchiveAttachment attachment,
        DateTime? timestamp)
    {
        Archive = archive;
        Attachment = attachment;
        Timestamp = timestamp;
    }

    public ArchiveMediaService Archive { get; }
    public ArchiveAttachment Attachment { get; }
    public DateTime? Timestamp { get; }
    public bool IsVideo => Attachment.Kind == AttachmentKind.Video;
    public string DateText => Timestamp?.ToString("MMM d, yyyy 'at' h:mm tt", CultureInfo.CurrentCulture) ??
                              "Date unavailable";
    public string ToolTipText => $"{Attachment.FileName}\n{DateText}\n{Attachment.SizeText}";

    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }
}
