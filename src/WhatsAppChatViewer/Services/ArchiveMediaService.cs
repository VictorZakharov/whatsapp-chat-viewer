using System.Collections.ObjectModel;
using System.IO.Compression;
using Avalonia.Media.Imaging;
using WhatsAppChatViewer.Models;

namespace WhatsAppChatViewer.Services;

public sealed class ArchiveMediaService : IDisposable
{
    private const long MaximumImageEntryLength = 256L * 1024 * 1024;
    private const int MaximumCachedGalleryThumbnails = 256;
    private static readonly int ThumbnailDecodeConcurrency = GetThumbnailDecodeConcurrency();
    private readonly FileStream _fileStream;
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _entriesByFullName;
    private readonly Dictionary<string, ZipArchiveEntry> _entriesByFileName;
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private readonly SemaphoreSlim _thumbnailLoadGate = new(ThumbnailDecodeConcurrency, ThumbnailDecodeConcurrency);
    private readonly SemaphoreSlim _videoThumbnailLoadGate = new(1, 1);
    private readonly SemaphoreSlim _temporaryMaterializationGate = new(1, 1);
    private readonly object _temporaryCopiesGate = new();
    private readonly Dictionary<string, string> _temporaryCopies = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _thumbnailCacheGate = new();
    private readonly Dictionary<string, CachedThumbnail> _thumbnailCache = new(StringComparer.OrdinalIgnoreCase);
    private string? _temporaryDirectory;
    private long _thumbnailAccessCounter;
    private bool _disposed;

    internal ArchiveMediaService(string archivePath)
    {
        ArchivePath = Path.GetFullPath(archivePath);
        _fileStream = new FileStream(
            ArchivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.RandomAccess);
        _archive = new ZipArchive(_fileStream, ZipArchiveMode.Read, leaveOpen: false);

        _entriesByFullName = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        _entriesByFileName = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            _entriesByFullName.TryAdd(NormalizeEntryName(entry.FullName), entry);
            _entriesByFileName.TryAdd(entry.Name, entry);
        }

        Entries = new ReadOnlyCollection<ArchiveEntryMetadata>(
            _archive.Entries
                .Where(static entry => !string.IsNullOrEmpty(entry.Name))
                .Select(static entry => new ArchiveEntryMetadata(
                    entry.FullName,
                    entry.Name,
                    entry.Length,
                    entry.CompressedLength))
                .ToList());
    }

    public string ArchivePath { get; }
    public IReadOnlyList<ArchiveEntryMetadata> Entries { get; }

    internal Stream OpenInitialRead(string entryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return GetEntry(entryName).Open();
    }

    public ArchiveAttachment? ResolveAttachment(string? referencedName)
    {
        if (string.IsNullOrWhiteSpace(referencedName))
        {
            return null;
        }

        var normalized = NormalizeEntryName(referencedName.Trim());
        if (!_entriesByFullName.TryGetValue(normalized, out var entry))
        {
            _entriesByFileName.TryGetValue(Path.GetFileName(normalized), out entry);
        }

        return entry is null
            ? null
            : new ArchiveAttachment(
                entry.Name,
                entry.FullName,
                ArchiveAttachment.Classify(entry.Name),
                entry.Length,
                entry.CompressedLength);
    }

    public Task<Bitmap> LoadBitmapAsync(
        ArchiveAttachment attachment,
        int targetWidth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetWidth, 1);
        if (attachment.Length > MaximumImageEntryLength)
        {
            throw new InvalidDataException(
                $"The image is too large to preview safely ({attachment.SizeText}).");
        }

        return Task.Run(() =>
        {
            MemoryStream? seekable = null;
            _readGate.Wait(cancellationToken);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                using var input = GetEntry(attachment.EntryName).Open();
                // ZipArchiveEntry streams are non-seekable. Skia can reject otherwise-valid
                // JPEGs on those streams, so buffer only this one lazily requested entry.
                seekable = new MemoryStream(checked((int)attachment.Length));
                var buffer = new byte[128 * 1024];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    seekable.Write(buffer, 0, read);
                }

                seekable.Position = 0;
            }
            catch
            {
                seekable?.Dispose();
                throw;
            }
            finally
            {
                _readGate.Release();
            }

            // Only access to the shared ZipArchive is serialized. Decoding is CPU-bound
            // and safe to run in parallel once this entry has its own seekable buffer.
            var imageBuffer = seekable ?? throw new InvalidDataException("The image entry could not be buffered.");
            using (imageBuffer)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Bitmap.DecodeToWidth(imageBuffer, targetWidth);
            }
        }, cancellationToken);
    }

    public async Task<GalleryThumbnailLease> AcquireGalleryThumbnailAsync(
        ArchiveAttachment attachment,
        int targetWidth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetWidth, 1);
        var cacheKey = $"{targetWidth}:{NormalizeEntryName(attachment.EntryName)}";
        lock (_thumbnailCacheGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thumbnailCache.TryGetValue(cacheKey, out var cached))
            {
                cached.ReferenceCount++;
                cached.LastAccess = ++_thumbnailAccessCounter;
                return new GalleryThumbnailLease(cached.Bitmap, () => ReleaseThumbnail(cacheKey));
            }
        }

        var loadGate = attachment.Kind == AttachmentKind.Video
            ? _videoThumbnailLoadGate
            : _thumbnailLoadGate;
        await loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Bitmap? bitmap = null;
        try
        {
            lock (_thumbnailCacheGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_thumbnailCache.TryGetValue(cacheKey, out var cached))
                {
                    cached.ReferenceCount++;
                    cached.LastAccess = ++_thumbnailAccessCounter;
                    return new GalleryThumbnailLease(cached.Bitmap, () => ReleaseThumbnail(cacheKey));
                }
            }

            bitmap = attachment.Kind == AttachmentKind.Video
                ? await LoadVideoThumbnailAsync(attachment, cancellationToken).ConfigureAwait(false)
                : await LoadBitmapAsync(attachment, targetWidth, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            lock (_thumbnailCacheGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_thumbnailCache.TryGetValue(cacheKey, out var cached))
                {
                    cached.ReferenceCount++;
                    cached.LastAccess = ++_thumbnailAccessCounter;
                    return new GalleryThumbnailLease(cached.Bitmap, () => ReleaseThumbnail(cacheKey));
                }

                _thumbnailCache.Add(
                    cacheKey,
                    new CachedThumbnail(bitmap, referenceCount: 1, ++_thumbnailAccessCounter));
                TrimThumbnailCache();
                var lease = new GalleryThumbnailLease(bitmap, () => ReleaseThumbnail(cacheKey));
                bitmap = null;
                return lease;
            }
        }
        finally
        {
            bitmap?.Dispose();
            loadGate.Release();
        }
    }

    public async Task CopyToAsync(
        ArchiveAttachment attachment,
        Stream destination,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        ArgumentNullException.ThrowIfNull(destination);

        await _readGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await using var input = GetEntry(attachment.EntryName).Open();
            var buffer = new byte[128 * 1024];
            long copied = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                copied += read;
                if (attachment.Length > 0)
                {
                    progress?.Report(Math.Min(1, (double)copied / attachment.Length));
                }
            }

            progress?.Report(1);
        }
        finally
        {
            _readGate.Release();
        }
    }

    public async Task<string> CreateTemporaryCopyAsync(
        ArchiveAttachment attachment,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        await _temporaryMaterializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (TryGetTemporaryCopy(attachment.EntryName, out var existing))
            {
                progress?.Report(1);
                return existing;
            }

            var directory = EnsureTemporaryDirectory();
            var safeName = SanitizeFileName(attachment.FileName);
            var destinationPath = Path.Combine(directory, $"{Guid.NewGuid():N}-{safeName}");

            try
            {
                await using (var destination = new FileStream(
                                 destinationPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.Read,
                                 bufferSize: 128 * 1024,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await CopyToAsync(attachment, destination, progress, cancellationToken)
                        .ConfigureAwait(false);
                }

                lock (_temporaryCopiesGate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    _temporaryCopies[attachment.EntryName] = destinationPath;
                    return destinationPath;
                }
            }
            catch
            {
                TryDeleteFile(destinationPath);
                throw;
            }
        }
        finally
        {
            _temporaryMaterializationGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _readGate.Wait();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _archive.Dispose();
            _fileStream.Dispose();
        }
        finally
        {
            _readGate.Release();
        }

        _temporaryMaterializationGate.Wait();
        for (var index = 0; index < ThumbnailDecodeConcurrency; index++)
        {
            _thumbnailLoadGate.Wait();
        }
        _videoThumbnailLoadGate.Wait();
        try
        {
            lock (_thumbnailCacheGate)
            {
                foreach (var cached in _thumbnailCache.Values)
                {
                    cached.Bitmap.Dispose();
                }

                _thumbnailCache.Clear();
            }

            string? temporaryDirectory;
            lock (_temporaryCopiesGate)
            {
                temporaryDirectory = _temporaryDirectory;
                _temporaryDirectory = null;
                _temporaryCopies.Clear();
            }

            if (temporaryDirectory is not null)
            {
                TryDeleteDirectory(temporaryDirectory);
            }
        }
        finally
        {
            _videoThumbnailLoadGate.Release();
            _thumbnailLoadGate.Release(ThumbnailDecodeConcurrency);
            _temporaryMaterializationGate.Release();
        }
    }

    private async Task<Bitmap> LoadVideoThumbnailAsync(
        ArchiveAttachment attachment,
        CancellationToken cancellationToken)
    {
        string? transientPath = null;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!TryGetTemporaryCopy(attachment.EntryName, out var sourcePath))
            {
                var extension = Path.GetExtension(attachment.FileName);
                if (string.IsNullOrWhiteSpace(extension) || extension.Length > 12)
                {
                    extension = ".video";
                }

                transientPath = Path.Combine(
                    EnsureTemporaryDirectory(),
                    $"thumbnail-{Guid.NewGuid():N}{extension}");
                await using (var destination = new FileStream(
                                 transientPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.Read,
                                 bufferSize: 128 * 1024,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await CopyToAsync(attachment, destination, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }

                sourcePath = transientPath;
            }

            return await VideoThumbnailExtractor.ExtractAsync(sourcePath, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (transientPath is not null)
            {
                TryDeleteFile(transientPath);
            }
        }
    }

    private ZipArchiveEntry GetEntry(string entryName)
    {
        if (_entriesByFullName.TryGetValue(NormalizeEntryName(entryName), out var entry))
        {
            return entry;
        }

        throw new FileNotFoundException("The attachment is no longer present in the archive.", entryName);
    }

    private string EnsureTemporaryDirectory()
    {
        lock (_temporaryCopiesGate)
        {
            if (_temporaryDirectory is not null)
            {
                return _temporaryDirectory;
            }

            var root = Path.Combine(Path.GetTempPath(), "WhatsAppChatViewer");
            var directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            _temporaryDirectory = directory;
            return directory;
        }
    }

    private bool TryGetTemporaryCopy(string entryName, out string path)
    {
        lock (_temporaryCopiesGate)
        {
            if (_temporaryCopies.TryGetValue(entryName, out var existing) && File.Exists(existing))
            {
                path = existing;
                return true;
            }

            path = string.Empty;
            return false;
        }
    }

    private void ReleaseThumbnail(string cacheKey)
    {
        lock (_thumbnailCacheGate)
        {
            if (_thumbnailCache.TryGetValue(cacheKey, out var cached))
            {
                cached.ReferenceCount = Math.Max(0, cached.ReferenceCount - 1);
                cached.LastAccess = ++_thumbnailAccessCounter;
            }

            TrimThumbnailCache();
        }
    }

    private void TrimThumbnailCache()
    {
        while (_thumbnailCache.Count > MaximumCachedGalleryThumbnails)
        {
            string? candidateKey = null;
            CachedThumbnail? candidate = null;
            foreach (var pair in _thumbnailCache)
            {
                if (pair.Value.ReferenceCount == 0 &&
                    (candidate is null || pair.Value.LastAccess < candidate.LastAccess))
                {
                    candidateKey = pair.Key;
                    candidate = pair.Value;
                }
            }

            if (candidateKey is null || candidate is null)
            {
                return;
            }

            _thumbnailCache.Remove(candidateKey);
            candidate.Bitmap.Dispose();
        }
    }

    private static string NormalizeEntryName(string name) => name.Replace('\\', '/').TrimStart('/');

    private static int GetThumbnailDecodeConcurrency()
    {
        var processorLimit = Math.Clamp(Environment.ProcessorCount / 2, 1, 12);
        var availableMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var memoryLimit = availableMemory > 0
            ? Math.Clamp((int)(availableMemory / (2L * 1024 * 1024 * 1024)), 1, 12)
            : 4;
        return Math.Min(processorLimit, memoryLimit);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(Path.GetFileName(name)
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "attachment" : sanitized;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class CachedThumbnail(Bitmap bitmap, int referenceCount, long lastAccess)
    {
        public Bitmap Bitmap { get; } = bitmap;
        public int ReferenceCount { get; set; } = referenceCount;
        public long LastAccess { get; set; } = lastAccess;
    }
}

public sealed record ArchiveEntryMetadata(string FullName, string Name, long Length, long CompressedLength);

public sealed class GalleryThumbnailLease : IDisposable
{
    private Action? _release;

    internal GalleryThumbnailLease(Bitmap bitmap, Action release)
    {
        Bitmap = bitmap;
        _release = release;
    }

    public Bitmap Bitmap { get; }

    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
