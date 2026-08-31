using WhatsAppChatViewer.Services;

namespace WhatsAppChatViewer.Tests;

public sealed class ArchiveDiscoveryTests
{
    [Fact]
    public void FindCandidates_ReturnsEveryZipOnceInStableNameOrder()
    {
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "WhatsAppChatViewerTests",
            Guid.NewGuid().ToString("N"));
        var appDirectory = Path.Combine(temporaryRoot, "src", "app", "bin", "Debug", "net10.0");
        Directory.CreateDirectory(appDirectory);
        var alpha = Path.Combine(temporaryRoot, "Alpha.zip");
        var beta = Path.Combine(temporaryRoot, "Beta.ZIP");
        File.WriteAllBytes(beta, []);
        File.WriteAllBytes(alpha, []);
        File.WriteAllText(Path.Combine(temporaryRoot, "notes.txt"), "not an archive");

        try
        {
            var result = ArchiveDiscovery.FindCandidates([], temporaryRoot, appDirectory);

            Assert.Equal([alpha, beta], result);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public void FindCandidates_UsesExplicitZipArgumentsInsteadOfDirectoryScan()
    {
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "WhatsAppChatViewerTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        var discovered = Path.Combine(temporaryRoot, "Discovered.zip");
        var requested = Path.Combine(temporaryRoot, "Requested.zip");
        File.WriteAllBytes(discovered, []);
        File.WriteAllBytes(requested, []);

        try
        {
            var result = ArchiveDiscovery.FindCandidates(
                [requested, requested],
                temporaryRoot,
                temporaryRoot);

            Assert.Equal([requested], result);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }
}
