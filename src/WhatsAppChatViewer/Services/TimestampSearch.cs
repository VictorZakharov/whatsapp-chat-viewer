using WhatsAppChatViewer.Models;

namespace WhatsAppChatViewer.Services;

public static class TimestampSearch
{
    public static int FindFirstAtOrAfter(IReadOnlyList<ChatMessage> messages, DateTime target)
    {
        if (messages.Count == 0)
        {
            return -1;
        }

        var low = 0;
        var high = messages.Count;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (messages[middle].Timestamp < target)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low < messages.Count ? low : messages.Count - 1;
    }
}
