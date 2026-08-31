namespace WhatsAppChatViewer.Models;

public sealed record ArchiveAttachment(
    string FileName,
    string EntryName,
    AttachmentKind Kind,
    long Length,
    long CompressedLength)
{
    public string SizeText => FileSizeFormatter.Format(Length);

    public static AttachmentKind Classify(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" or ".png" or ".heic" or ".heif" or ".bmp" => AttachmentKind.Image,
            ".webp" => fileName.StartsWith("STK-", StringComparison.OrdinalIgnoreCase)
                ? AttachmentKind.Sticker
                : AttachmentKind.Image,
            ".gif" => AttachmentKind.AnimatedImage,
            ".mp4" or ".mov" or ".mkv" or ".webm" or ".3gp" => AttachmentKind.Video,
            ".opus" or ".ogg" or ".mp3" or ".m4a" or ".aac" or ".wav" => AttachmentKind.Audio,
            ".vcf" => AttachmentKind.Contact,
            ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" or ".zip" => AttachmentKind.Document,
            _ => AttachmentKind.Unknown
        };
    }
}

internal static class FileSizeFormatter
{
    public static string Format(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.#} {units[unit]}";
    }
}
