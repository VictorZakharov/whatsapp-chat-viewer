namespace WhatsAppChatViewer.Services;

public static class ArchiveDiscovery
{
    public static IReadOnlyList<string> FindCandidates(
        IEnumerable<string> arguments,
        string currentDirectory,
        string applicationBaseDirectory)
    {
        var argumentArchives = arguments
            .Where(static path => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (argumentArchives.Length > 0)
        {
            return argumentArchives;
        }

        var directories = new List<string> { currentDirectory, applicationBaseDirectory };
        var parent = new DirectoryInfo(applicationBaseDirectory);
        for (var depth = 0; depth < 5 && parent.Parent is not null; depth++)
        {
            parent = parent.Parent;
            directories.Add(parent.FullName);
        }

        var archives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                foreach (var archive in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                             .Where(static path =>
                                 Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)))
                {
                    archives.Add(Path.GetFullPath(archive));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return archives
            .OrderBy(static path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(static path => path, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }
}
