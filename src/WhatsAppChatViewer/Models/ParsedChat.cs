namespace WhatsAppChatViewer.Models;

public sealed record ParsedChat(
    IReadOnlyList<ParsedChatMessage> Messages,
    IReadOnlyDictionary<string, int> SenderCounts);

public sealed record ParsedChatMessage(
    DateTime Timestamp,
    string? Sender,
    string Text,
    bool IsSystem,
    bool IsEdited,
    string? AttachmentName);
