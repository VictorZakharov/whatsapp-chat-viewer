namespace WhatsAppChatViewer.Models;

public sealed record LoadProgress(string Stage, int MessageCount = 0);
