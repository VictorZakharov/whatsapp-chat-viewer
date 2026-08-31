using System.IO.Compression;
using System.Text;
using WhatsAppChatViewer.ViewModels;

namespace WhatsAppChatViewer.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task LoadArchivesAsync_KeepsMultipleChatsOpenAndSelectable()
    {
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "WhatsAppChatViewerTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        var aliceArchive = Path.Combine(temporaryRoot, "alice.zip");
        var bobArchive = Path.Combine(temporaryRoot, "bob.zip");
        CreateChatArchive(aliceArchive, "Alice", "hello");
        CreateChatArchive(bobArchive, "Bob", "hi");

        try
        {
            using (var viewModel = new MainWindowViewModel())
            {
                await viewModel.LoadArchivesAsync([aliceArchive, bobArchive], replaceExisting: true);

                Assert.Equal(2, viewModel.Chats.Count);
                Assert.Equal("Alice", viewModel.SelectedChat?.Title);
                Assert.Equal("Alice", viewModel.Title);

                viewModel.SelectedChat = viewModel.Chats[1];

                Assert.Equal("Bob", viewModel.Title);
                Assert.NotEmpty(viewModel.Items);
            }

            File.Delete(aliceArchive);
            File.Delete(bobArchive);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task LoadArchivesAsync_OpensValidChatsAndReportsInvalidZips()
    {
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "WhatsAppChatViewerTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        var validArchive = Path.Combine(temporaryRoot, "valid.zip");
        var invalidArchive = Path.Combine(temporaryRoot, "not-whatsapp.zip");
        CreateChatArchive(validArchive, "Alice", "hello");
        using (ZipFile.Open(invalidArchive, ZipArchiveMode.Create))
        {
        }

        try
        {
            using var viewModel = new MainWindowViewModel();

            await viewModel.LoadArchivesAsync([validArchive, invalidArchive], replaceExisting: true);

            Assert.Single(viewModel.Chats);
            Assert.True(viewModel.HasChat);
            Assert.Contains("not-whatsapp.zip", viewModel.ErrorText);
            Assert.Contains("WhatsApp chat text file", viewModel.ErrorText);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static void CreateChatArchive(string archivePath, string contact, string message)
    {
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry($"WhatsApp Chat with {contact}.txt");
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write($"2026-08-30, 09:42 - {contact}: {message}\n");
    }
}
