using System.Diagnostics;
using WhatsAppChatViewer.Services;
using Xunit.Abstractions;

namespace WhatsAppChatViewer.Tests;

public sealed class IncludedArchiveSmokeTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Integration")]
    public void IncludedExport_CanBeIndexedDirectlyInPlace()
    {
        var repositoryRoot = FindRepositoryRoot();
        var export = Directory.EnumerateFiles(repositoryRoot, "*.zip", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
        if (export is null)
        {
            output.WriteLine("No local export ZIP is present; smoke test skipped.");
            return;
        }

        var before = GC.GetTotalMemory(forceFullCollection: true);
        var stopwatch = Stopwatch.StartNew();
        using var session = new WhatsAppArchiveLoader().Load(export);
        stopwatch.Stop();
        var retained = GC.GetTotalMemory(forceFullCollection: false) - before;

        output.WriteLine("Archive: {0:N0} bytes", new FileInfo(export).Length);
        output.WriteLine("Messages: {0:N0}", session.Messages.Count);
        output.WriteLine("Attachments: {0:N0}", session.AttachmentCount);
        output.WriteLine("Index time: {0:N2}s", stopwatch.Elapsed.TotalSeconds);
        output.WriteLine("Approx. retained managed memory: {0:N1} MB", retained / 1024d / 1024d);

        Assert.True(session.Messages.Count > 50_000);
        Assert.True(session.AttachmentCount > 1_000);
        Assert.NotEmpty(session.Participants);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMinutes(1));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WhatsAppChatViewer.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
