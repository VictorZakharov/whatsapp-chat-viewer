using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using LibVLCSharp.Shared;

namespace WhatsAppChatViewer.Services;

internal sealed class VideoThumbnailExtractor : IDisposable
{
    private const uint MaximumWidth = 180;
    private const uint MaximumHeight = 120;
    private const uint BytesPerPixel = 4;
    private readonly object _bufferGate = new();
    private readonly TaskCompletionSource<FrameData> _frameReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IntPtr _buffer;
    private uint _width;
    private uint _height;
    private uint _pitch;
    private int _displayedFrames;
    private bool _disposed;

    public static async Task<Bitmap> ExtractAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Core.Initialize();

        using var extractor = new VideoThumbnailExtractor();
        using var libVlc = new LibVLC("--no-video-title-show", "--quiet", "--no-audio");
        using var player = new MediaPlayer(libVlc)
        {
            EnableKeyInput = false,
            EnableMouseInput = false,
            Mute = true
        };
        using var media = new Media(libVlc, new Uri(path));
        media.AddOption(":no-audio");

        player.SetVideoFormatCallbacks(extractor.ConfigureFormat, extractor.CleanupFormat);
        player.SetVideoCallbacks(extractor.LockVideo, null, extractor.DisplayVideo);
        player.EncounteredError += (_, _) =>
            extractor._frameReady.TrySetException(
                new InvalidDataException("The video thumbnail could not be decoded."));
        player.EndReached += (_, _) =>
            extractor._frameReady.TrySetException(
                new InvalidDataException("The video ended before a thumbnail was available."));

        if (!player.Play(media))
        {
            throw new InvalidDataException("The video thumbnail could not be started.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        FrameData frame;
        try
        {
            frame = await extractor._frameReady.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidDataException("Timed out while decoding the video thumbnail.");
        }
        finally
        {
            try
            {
                player.Stop();
            }
            catch
            {
                // The native decoder may already have stopped after an error or very short file.
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return CreateBitmap(frame);
    }

    public void Dispose()
    {
        lock (_bufferGate)
        {
            _disposed = true;
            FreeBuffer();
        }
    }

    private uint ConfigureFormat(
        ref IntPtr opaque,
        IntPtr chroma,
        ref uint width,
        ref uint height,
        ref uint pitches,
        ref uint lines)
    {
        var sourceWidth = Math.Max(1u, width);
        var sourceHeight = Math.Max(1u, height);
        var scale = Math.Min(
            MaximumWidth / (double)sourceWidth,
            MaximumHeight / (double)sourceHeight);
        scale = Math.Min(1, scale);

        var outputWidth = Math.Max(1u, (uint)Math.Round(sourceWidth * scale));
        var outputHeight = Math.Max(1u, (uint)Math.Round(sourceHeight * scale));
        var outputPitch = Align(outputWidth * BytesPerPixel);
        var outputLines = Align(outputHeight);

        lock (_bufferGate)
        {
            if (_disposed)
            {
                return 0;
            }

            FreeBuffer();
            _width = outputWidth;
            _height = outputHeight;
            _pitch = outputPitch;
            _buffer = Marshal.AllocHGlobal(checked((int)(outputPitch * outputLines)));
        }

        Marshal.Copy("RV32"u8.ToArray(), 0, chroma, 4);
        width = outputWidth;
        height = outputHeight;
        pitches = outputPitch;
        lines = outputLines;
        return 1;
    }

    private void CleanupFormat(ref IntPtr opaque)
    {
        lock (_bufferGate)
        {
            FreeBuffer();
        }
    }

    private IntPtr LockVideo(IntPtr opaque, IntPtr planes)
    {
        lock (_bufferGate)
        {
            Marshal.WriteIntPtr(planes, _buffer);
        }

        return IntPtr.Zero;
    }

    private void DisplayVideo(IntPtr opaque, IntPtr picture)
    {
        // Waiting a handful of decoded frames avoids the black lead-in common in phone videos.
        if (Interlocked.Increment(ref _displayedFrames) < 5 || _frameReady.Task.IsCompleted)
        {
            return;
        }

        lock (_bufferGate)
        {
            if (_disposed || _buffer == IntPtr.Zero || _frameReady.Task.IsCompleted)
            {
                return;
            }

            var length = checked((int)(_pitch * _height));
            var pixels = new byte[length];
            Marshal.Copy(_buffer, pixels, 0, length);
            _frameReady.TrySetResult(new FrameData(pixels, _width, _height, _pitch));
        }
    }

    private void FreeBuffer()
    {
        if (_buffer == IntPtr.Zero)
        {
            return;
        }

        Marshal.FreeHGlobal(_buffer);
        _buffer = IntPtr.Zero;
    }

    private static uint Align(uint value) =>
        value % 32 == 0 ? value : ((value / 32) + 1) * 32;

    private static Bitmap CreateBitmap(FrameData frame)
    {
        var width = checked((int)frame.Width);
        var height = checked((int)frame.Height);
        var sourcePitch = checked((int)frame.Pitch);
        var destinationPitch = checked(width * (int)BytesPerPixel);
        var pixelsLength = checked(destinationPitch * height);
        var bytes = new byte[checked(54 + pixelsLength)];

        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2, 4), bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10, 4), 54);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14, 4), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22, 4), -height);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26, 2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(28, 2), 32);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(34, 4), pixelsLength);

        for (var row = 0; row < height; row++)
        {
            Buffer.BlockCopy(
                frame.Pixels,
                row * sourcePitch,
                bytes,
                54 + (row * destinationPitch),
                destinationPitch);
        }

        using var stream = new MemoryStream(bytes, writable: false);
        return new Bitmap(stream);
    }

    private sealed record FrameData(byte[] Pixels, uint Width, uint Height, uint Pitch);
}
