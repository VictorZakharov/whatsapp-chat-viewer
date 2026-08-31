using System.Globalization;
using WhatsAppChatViewer.Models;

namespace WhatsAppChatViewer.ViewModels;

public sealed class ChatListItemViewModel
{
    public ChatListItemViewModel(ChatSession session)
    {
        Session = session;
        Title = session.Title;
        ContactInitial = string.IsNullOrWhiteSpace(Title)
            ? "W"
            : StringInfo.GetNextTextElement(Title).ToUpperInvariant();
        ArchiveName = Path.GetFileName(session.ArchivePath);
        ArchiveDetails = $"{FileSizeFormatter.Format(new FileInfo(session.ArchivePath).Length)} · opened without extraction";
        MessageCountText = session.Messages.Count.ToString("N0");
        MediaCountText = session.AttachmentCount.ToString("N0");
        RangeText = session.Messages.Count == 0
            ? "No messages"
            : $"{session.Messages[0].Timestamp:MMM d, yyyy} — {session.Messages[^1].Timestamp:MMM d, yyyy}";
        LastMessagePreview = GetLastMessagePreview(session.Messages);
        MediaGallery = session.Messages
            .Where(static message => message.Attachment?.Kind is
                AttachmentKind.Image or
                AttachmentKind.Sticker or
                AttachmentKind.AnimatedImage or
                AttachmentKind.Video)
            .Select(static message => new MediaGalleryEntry(message.Attachment!, message.Timestamp))
            .DistinctBy(static entry => entry.Attachment.EntryName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal ChatSession Session { get; }
    public string Title { get; }
    public string ContactInitial { get; }
    public string ArchiveName { get; }
    public string ArchiveDetails { get; }
    public string MessageCountText { get; }
    public string MediaCountText { get; }
    public string RangeText { get; }
    public string LastMessagePreview { get; }
    public IReadOnlyList<MediaGalleryEntry> MediaGallery { get; }
    public string? SelectedIdentity { get; set; }

    private static string GetLastMessagePreview(IReadOnlyList<ChatMessage> messages)
    {
        var last = messages.LastOrDefault(static message => !message.IsSystem);
        if (last is null)
        {
            return "No messages";
        }

        var preview = !string.IsNullOrWhiteSpace(last.Text)
            ? last.Text.ReplaceLineEndings(" ")
            : last.Attachment?.Kind switch
            {
                AttachmentKind.Image or AttachmentKind.Sticker => "📷 Photo",
                AttachmentKind.Video => "▶ Video",
                AttachmentKind.Audio => "🎙 Voice message",
                _ => "Attachment"
            };
        return preview.Length <= 54 ? preview : $"{preview[..53]}…";
    }
}
