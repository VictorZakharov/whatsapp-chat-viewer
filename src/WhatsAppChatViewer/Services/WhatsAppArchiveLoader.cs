using WhatsAppChatViewer.Models;

namespace WhatsAppChatViewer.Services;

public sealed class WhatsAppArchiveLoader
{
    private const long MaximumChatTextLength = 256L * 1024 * 1024;
    private readonly WhatsAppChatParser _parser = new();

    public ChatSession Load(
        string archivePath,
        IProgress<LoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("The selected WhatsApp export does not exist.", archivePath);
        }

        progress?.Report(new LoadProgress("Opening ZIP directory…"));
        var media = new ArchiveMediaService(archivePath);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chatEntry = FindChatEntry(media.Entries);
            if (chatEntry.Length > MaximumChatTextLength)
            {
                throw new InvalidDataException(
                    $"The chat text entry is unexpectedly large ({FileSizeFormatter.Format(chatEntry.Length)}).");
            }

            progress?.Report(new LoadProgress("Reading messages…"));
            ParsedChat parsed;
            using (var chatStream = media.OpenInitialRead(chatEntry.FullName))
            {
                parsed = _parser.Parse(chatStream, progress, cancellationToken);
            }

            var messages = new List<ChatMessage>(parsed.Messages.Count);
            for (var index = 0; index < parsed.Messages.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = parsed.Messages[index];
                messages.Add(new ChatMessage(
                    index,
                    item.Timestamp,
                    item.Sender,
                    item.Text,
                    item.IsSystem,
                    item.IsEdited,
                    item.AttachmentName,
                    media.ResolveAttachment(item.AttachmentName)));
            }

            var participants = parsed.SenderCounts
                .OrderByDescending(static pair => pair.Value)
                .ThenBy(static pair => pair.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(static pair => pair.Key)
                .ToArray();

            var title = GetChatTitle(chatEntry.Name);
            var attachmentCount = media.Entries.Count(static entry =>
                !entry.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));

            progress?.Report(new LoadProgress("Ready", messages.Count));
            return new ChatSession(
                Path.GetFullPath(archivePath),
                title,
                chatEntry.FullName,
                messages,
                participants,
                attachmentCount,
                media);
        }
        catch
        {
            media.Dispose();
            throw;
        }
    }

    private static ArchiveEntryMetadata FindChatEntry(IReadOnlyList<ArchiveEntryMetadata> entries)
    {
        var candidates = entries
            .Where(static entry => entry.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (candidates.Length == 0)
        {
            throw new InvalidDataException("The ZIP does not contain a WhatsApp chat text file.");
        }

        return candidates
            .OrderByDescending(static entry =>
                entry.Name.StartsWith("WhatsApp Chat", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(static entry => entry.Length)
            .First();
    }

    private static string GetChatTitle(string textEntryName)
    {
        var title = Path.GetFileNameWithoutExtension(textEntryName);
        const string prefix = "WhatsApp Chat with ";
        return title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? title[prefix.Length..]
            : title;
    }
}
