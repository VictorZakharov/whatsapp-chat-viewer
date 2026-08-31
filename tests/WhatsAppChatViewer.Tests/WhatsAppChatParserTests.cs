using System.Text;
using WhatsAppChatViewer.Services;

namespace WhatsAppChatViewer.Tests;

public sealed class WhatsAppChatParserTests
{
    [Fact]
    public void Parse_HandlesMessagesMultilineAttachmentsAndSystemEvents()
    {
        const string export = """
            2025-10-18, 09:42 - Messages and calls are end-to-end encrypted.
            2025-10-18, 09:43 - Alice: First line
            second line: still the same message
            2025-10-18, 09:44 - Bob: IMG-20251018-WA0001.jpg (file attached)
            A caption below the attachment
            2025-10-18, 09:45 - Alice: Fixed text <This message was edited>
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(export));
        var result = new WhatsAppChatParser().Parse(stream);

        Assert.Equal(4, result.Messages.Count);
        Assert.True(result.Messages[0].IsSystem);
        Assert.Equal("Alice", result.Messages[1].Sender);
        Assert.Equal("First line\nsecond line: still the same message", result.Messages[1].Text);
        Assert.Equal("IMG-20251018-WA0001.jpg", result.Messages[2].AttachmentName);
        Assert.Equal("A caption below the attachment", result.Messages[2].Text);
        Assert.True(result.Messages[3].IsEdited);
        Assert.Equal("Fixed text", result.Messages[3].Text);
    }

    [Theory]
    [InlineData("18/10/2025, 9:42 am - Alice: hello")]
    [InlineData("10/18/2025, 9:42 AM - Alice: hello")]
    [InlineData("[2025-10-18, 09:42] - Alice: hello")]
    public void Parse_AcceptsCommonExportDateFormats(string line)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(line));

        var result = new WhatsAppChatParser().Parse(stream);

        var message = Assert.Single(result.Messages);
        Assert.Equal("Alice", message.Sender);
        Assert.Equal("hello", message.Text);
    }

    [Fact]
    public void Parse_HandlesIosAttachmentMarker()
    {
        const string export = "2025-10-18, 09:44 - Bob: <attached: PTT-20251018-WA0004.opus>\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(export));

        var message = Assert.Single(new WhatsAppChatParser().Parse(stream).Messages);

        Assert.Equal("PTT-20251018-WA0004.opus", message.AttachmentName);
        Assert.Empty(message.Text);
    }
}
