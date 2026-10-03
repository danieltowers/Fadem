namespace Faderio.Core.Playback;

/// <summary>
/// A file's bytes held in memory, read-only: what a decoder reads when its track must never
/// wait on the disk (phase 6 issue 10). The bytes are the file's own, still compressed. They are
/// kept in small chunks, never one run: the plugin's process is 32-bit and shared with MusicBee
/// (ADR 0012), where a contiguous run the size of a file may not be found.
/// </summary>
internal sealed class MemoryFile : Stream
{
    /// <summary>Below the large-object threshold, so the chunks are ordinary, movable objects.</summary>
    private const int ChunkBytes = 64 * 1024;

    private readonly List<byte[]> _chunks;
    private readonly long _length;
    private long _position;

    private MemoryFile(List<byte[]> chunks, long length)
    {
        _chunks = chunks;
        _length = length;
    }

    /// <summary>Reads <paramref name="file"/> from where it stands to its end. Stops with
    /// <see cref="OperationCanceledException"/> between chunks once <paramref name="cancelled"/>.</summary>
    public static MemoryFile ReadToEnd(Stream file, CancellationToken cancelled)
    {
        var chunks = new List<byte[]>();
        long length = 0;
        while (true)
        {
            cancelled.ThrowIfCancellationRequested();
            var chunk = new byte[ChunkBytes];
            var filled = 0;
            int read;
            while (filled < ChunkBytes && (read = file.Read(chunk, filled, ChunkBytes - filled)) > 0)
            {
                filled += read;
            }
            if (filled == 0)
            {
                break;
            }
            chunks.Add(chunk);
            length += filled;
            if (filled < ChunkBytes)
            {
                break; // the end of the file
            }
        }
        return new MemoryFile(chunks, length);
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => _position = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var total = (int)Math.Max(0, Math.Min(count, _length - _position));
        var copied = 0;
        while (copied < total)
        {
            var within = (int)(_position % ChunkBytes);
            var run = Math.Min(total - copied, ChunkBytes - within);
            Buffer.BlockCopy(_chunks[(int)(_position / ChunkBytes)], within, buffer, offset + copied, run);
            copied += run;
            _position += run;
        }
        return total;
    }

    public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
    {
        SeekOrigin.Begin => offset,
        SeekOrigin.Current => _position + offset,
        _ => _length + offset,
    };

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
