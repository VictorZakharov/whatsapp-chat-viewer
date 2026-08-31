using WhatsAppChatViewer.Services;

namespace WhatsAppChatViewer.Models;

public sealed class ChatSession : IDisposable
{
    public ChatSession(
        string archivePath,
        string title,
        string textEntryName,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<string> participants,
        int attachmentCount,
        ArchiveMediaService media)
    {
        ArchivePath = archivePath;
        Title = title;
        TextEntryName = textEntryName;
        Messages = messages;
        Participants = participants;
        AttachmentCount = attachmentCount;
        Media = media;
    }

    public string ArchivePath { get; }
    public string Title { get; }
    public string TextEntryName { get; }
    public IReadOnlyList<ChatMessage> Messages { get; }
    public IReadOnlyList<string> Participants { get; }
    public int AttachmentCount { get; }
    public ArchiveMediaService Media { get; }

    public void Dispose() => Media.Dispose();
}
