namespace WhatsAppChatViewer.Models;

public sealed record AttachmentOpenRequest(
    ArchiveAttachment Attachment,
    bool StartFullscreen = false,
    VideoPlaybackState? VideoState = null);

public sealed record VideoPlaybackState(
    int RotationDegrees,
    long PositionMilliseconds,
    bool IsPaused,
    int Volume,
    bool IsMuted);
