namespace WhatsAppChatViewer.Models;

public sealed record ChatMessage(
    int Index,
    DateTime Timestamp,
    string? Sender,
    string Text,
    bool IsSystem,
    bool IsEdited,
    string? ReferencedAttachmentName,
    ArchiveAttachment? Attachment);
