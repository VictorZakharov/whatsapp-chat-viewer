using System.IO.Compression;
using System.Text;
using WhatsAppChatViewer.Models;
using WhatsAppChatViewer.Services;

namespace WhatsAppChatViewer.Tests;

public sealed class WhatsAppArchiveLoaderTests
{
    [Fact]
    public void Load_IndexesTextAndResolvesMediaWithoutExtractingArchive()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "WhatsAppChatViewerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        var archivePath = Path.Combine(temporaryRoot, "chat.zip");

        try
        {
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                WriteEntry(
                    archive,
                    "WhatsApp Chat with Alice.txt",
                    "2025-10-18, 09:42 - Alice: hello\n" +
                    "2025-10-18, 09:43 - Bob: IMG-20251018-WA0001.jpg (file attached)\n");
                WriteEntry(archive, "IMG-20251018-WA0001.jpg", "not-an-image-but-indexable");
            }

            using var session = new WhatsAppArchiveLoader().Load(archivePath);

            Assert.Equal("Alice", session.Title);
            Assert.Equal(2, session.Messages.Count);
            Assert.Equal(1, session.AttachmentCount);
            Assert.Equal(AttachmentKind.Image, session.Messages[1].Attachment?.Kind);
            Assert.Single(Directory.GetFiles(temporaryRoot));
            Assert.Empty(Directory.GetDirectories(temporaryRoot));
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CreateTemporaryCopyAsync_MaterializesOnlyRequestedEntryAndCleansItOnDispose()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "WhatsAppChatViewerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        var archivePath = Path.Combine(temporaryRoot, "chat.zip");
        var expected = Enumerable.Range(0, 256).Select(static value => (byte)value).ToArray();
        string? materializedPath = null;

        try
        {
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                WriteEntry(
                    archive,
                    "WhatsApp Chat with Alice.txt",
                    "2025-10-18, 09:42 - Alice: clip.mp4 (file attached)\n");
                var video = archive.CreateEntry("clip.mp4", CompressionLevel.Fastest);
                await using var output = video.Open();
                await output.WriteAsync(expected);
            }

            using (var session = new WhatsAppArchiveLoader().Load(archivePath))
            {
                var attachment = Assert.IsType<ArchiveAttachment>(session.Messages[0].Attachment);
                materializedPath = await session.Media.CreateTemporaryCopyAsync(attachment);
                var cachedPath = await session.Media.CreateTemporaryCopyAsync(attachment);

                Assert.Equal(materializedPath, cachedPath);
                Assert.Equal(expected, await File.ReadAllBytesAsync(materializedPath));
                Assert.Single(Directory.GetFiles(temporaryRoot));
                Assert.Empty(Directory.GetDirectories(temporaryRoot));
            }

            Assert.False(File.Exists(materializedPath));
            Assert.False(Directory.Exists(Path.GetDirectoryName(materializedPath)));
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CreateTemporaryCopyAsync_ConcurrentRequestsShareOneMaterialization()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "WhatsAppChatViewerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        var archivePath = Path.Combine(temporaryRoot, "chat.zip");
        var expected = Enumerable.Range(0, 1024 * 256)
            .Select(static value => (byte)(value % 251))
            .ToArray();

        try
        {
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                WriteEntry(
                    archive,
                    "WhatsApp Chat with Alice.txt",
                    "2025-10-18, 09:42 - Alice: clip.mp4 (file attached)\n");
                var video = archive.CreateEntry("clip.mp4", CompressionLevel.Fastest);
                await using var output = video.Open();
                await output.WriteAsync(expected);
            }

            using var session = new WhatsAppArchiveLoader().Load(archivePath);
            var attachment = Assert.IsType<ArchiveAttachment>(session.Messages[0].Attachment);
            var requests = Enumerable.Range(0, 8)
                .Select(_ => session.Media.CreateTemporaryCopyAsync(attachment))
                .ToArray();

            var paths = await Task.WhenAll(requests);

            Assert.Single(paths.Distinct(StringComparer.OrdinalIgnoreCase));
            Assert.Equal(expected, await File.ReadAllBytesAsync(paths[0]));
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
