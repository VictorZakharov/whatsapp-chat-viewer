using WhatsAppChatViewer.Models;
using WhatsAppChatViewer.Services;

namespace WhatsAppChatViewer.Tests;

public sealed class TimestampSearchTests
{
    private static readonly IReadOnlyList<ChatMessage> Messages =
    [
        Message(0, 9, 0),
        Message(1, 9, 30),
        Message(2, 10, 0),
        Message(3, 12, 15)
    ];

    [Fact]
    public void FindFirstAtOrAfter_ReturnsExactMatch() =>
        Assert.Equal(1, TimestampSearch.FindFirstAtOrAfter(Messages, At(9, 30)));

    [Fact]
    public void FindFirstAtOrAfter_ReturnsNextMessageAcrossGap() =>
        Assert.Equal(2, TimestampSearch.FindFirstAtOrAfter(Messages, At(9, 31)));

    [Fact]
    public void FindFirstAtOrAfter_ClampsAfterConversationEnd() =>
        Assert.Equal(3, TimestampSearch.FindFirstAtOrAfter(Messages, At(23, 0)));

    [Fact]
    public void FindFirstAtOrAfter_ReturnsMinusOneForEmptyList() =>
        Assert.Equal(-1, TimestampSearch.FindFirstAtOrAfter([], At(9, 0)));

    private static ChatMessage Message(int index, int hour, int minute) =>
        new(index, At(hour, minute), "Alice", "text", false, false, null, null);

    private static DateTime At(int hour, int minute) => new(2025, 10, 18, hour, minute, 0);
}
