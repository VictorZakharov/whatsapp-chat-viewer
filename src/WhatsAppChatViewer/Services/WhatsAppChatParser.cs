using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using WhatsAppChatViewer.Models;

namespace WhatsAppChatViewer.Services;

public sealed partial class WhatsAppChatParser
{
    private static readonly string[] TimestampFormats =
    [
        "yyyy-MM-dd H:mm", "yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm:ss", "yyyy-MM-dd HH:mm:ss",
        "d/M/yyyy H:mm", "d/M/yyyy HH:mm", "d/M/yy H:mm", "d/M/yy HH:mm",
        "d/M/yyyy h:mm tt", "d/M/yy h:mm tt",
        "M/d/yyyy H:mm", "M/d/yyyy HH:mm", "M/d/yy H:mm", "M/d/yy HH:mm",
        "M/d/yyyy h:mm tt", "M/d/yy h:mm tt",
        "d.M.yyyy H:mm", "d.M.yy H:mm", "d-M-yyyy H:mm", "d-M-yy H:mm"
    ];

    public ParsedChat Parse(
        Stream chatStream,
        IProgress<LoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chatStream);

        var messages = new List<ParsedChatMessage>(32_768);
        var senders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StreamReader(
            chatStream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false),
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 64 * 1024,
            leaveOpen: true);

        PendingMessage? current = null;
        string? line;
        var lineNumber = 0;

        while ((line = reader.ReadLine()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;

            if (TryReadHeader(line, out var timestamp, out var payload))
            {
                if (current is not null)
                {
                    AddMessage(current, messages, senders);
                }

                current = CreatePending(timestamp, payload);
                if (messages.Count > 0 && messages.Count % 10_000 == 0)
                {
                    progress?.Report(new LoadProgress("Reading messages…", messages.Count));
                }
            }
            else if (current is not null)
            {
                current.Body.Append('\n').Append(line);
            }
        }

        if (current is not null)
        {
            AddMessage(current, messages, senders);
        }

        progress?.Report(new LoadProgress("Indexing conversation…", messages.Count));
        return new ParsedChat(messages, senders);
    }

    private static PendingMessage CreatePending(DateTime timestamp, string payload)
    {
        var colon = payload.IndexOf(": ", StringComparison.Ordinal);
        if (colon is > 0 and <= 120)
        {
            var sender = payload[..colon].Trim();
            if (sender.Length > 0)
            {
                return new PendingMessage(timestamp, sender, payload[(colon + 2)..], isSystem: false);
            }
        }

        return new PendingMessage(timestamp, sender: null, payload, isSystem: true);
    }

    private static void AddMessage(
        PendingMessage pending,
        ICollection<ParsedChatMessage> messages,
        IDictionary<string, int> senders)
    {
        var body = pending.Body.ToString().TrimEnd();
        var edited = EditedMarkerRegex().IsMatch(body);
        if (edited)
        {
            body = EditedMarkerRegex().Replace(body, string.Empty).TrimEnd();
        }

        string? attachmentName = null;
        var match = AttachedFileRegex().Match(body);
        if (!match.Success)
        {
            match = IosAttachedFileRegex().Match(body);
        }

        if (match.Success)
        {
            attachmentName = StripDirectionalMarks(match.Groups["file"].Value.Trim());
            body = match.Groups["caption"].Success
                ? match.Groups["caption"].Value.Trim()
                : string.Empty;
        }

        messages.Add(new ParsedChatMessage(
            pending.Timestamp,
            pending.Sender,
            body,
            pending.IsSystem,
            edited,
            attachmentName));

        if (pending.Sender is not null)
        {
            senders[pending.Sender] = senders.TryGetValue(pending.Sender, out var count) ? count + 1 : 1;
        }
    }

    private static bool TryReadHeader(string line, out DateTime timestamp, out string payload)
    {
        timestamp = default;
        payload = string.Empty;

        var match = HeaderRegex().Match(StripDirectionalMarks(line));
        if (!match.Success)
        {
            return false;
        }

        var rawTimestamp = $"{match.Groups["date"].Value} {match.Groups["time"].Value}"
            .Replace('\u202f', ' ')
            .Replace('\u00a0', ' ');

        if (!DateTime.TryParseExact(
                rawTimestamp,
                TimestampFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out timestamp) &&
            !DateTime.TryParse(rawTimestamp, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out timestamp))
        {
            return false;
        }

        payload = match.Groups["payload"].Value;
        return true;
    }

    private static string StripDirectionalMarks(string value) =>
        value.TrimStart('\u200e', '\u200f', '\u202a', '\u202b', '\u202c', '\u202d', '\u202e', '\ufeff');

    [GeneratedRegex(@"^\[?(?<date>\d{1,4}[-/.]\d{1,2}[-/.]\d{1,4}),?\s+(?<time>\d{1,2}:\d{2}(?::\d{2})?(?:[\s\u00a0\u202f]?[AaPp][Mm])?)\]?\s+-\s+(?<payload>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex HeaderRegex();

    [GeneratedRegex(@"\s*<This message was edited>\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex EditedMarkerRegex();

    [GeneratedRegex(@"^(?<file>[^\r\n]+?)\s+\(file attached\)\s*(?:\r?\n(?<caption>[\s\S]*))?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex AttachedFileRegex();

    [GeneratedRegex(@"^\s*<?attached:\s*(?<file>[^>\r\n]+)>?\s*(?:\r?\n(?<caption>[\s\S]*))?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex IosAttachedFileRegex();

    private sealed class PendingMessage(DateTime timestamp, string? sender, string body, bool isSystem)
    {
        public DateTime Timestamp { get; } = timestamp;
        public string? Sender { get; } = sender;
        public StringBuilder Body { get; } = new(body);
        public bool IsSystem { get; } = isSystem;
    }
}
