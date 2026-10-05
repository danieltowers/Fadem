using NAudio.Wave;

namespace Faderio.Core.Playback;

/// <summary>
/// A WMA file through the Windows Media Format runtime (issue 12), in both hosts. Reads are the
/// runtime's decode from the start. A seek asks the runtime for the sample before the target,
/// takes the frame its second sample starts at from the file's <see cref="WmaIndex"/>, and
/// discards to the exact sample: it costs milliseconds anywhere in the file, where decoding
/// forward from the start costs seconds near the end of a long one. A seek whose samples the
/// index does not recognise decodes forward from the start instead, exact but slower.
/// </summary>
internal sealed class WmaTrackReader : ITrackSource
{
    private readonly Stream _file;
    private readonly string _name;
    private readonly WmaIndex _index;
    private WmSyncReader _reader;

    /// <summary>Decoded bytes not yet read, from <see cref="_pendingAt"/>; the frame they start
    /// at is <see cref="Position"/>.</summary>
    private byte[] _pending = new byte[0];
    private int _pendingAt;
    private int _pendingBytes;
    private byte[] _next = new byte[0];
    private long _position;
    private bool _disposed;

    private WmaTrackReader(Stream file, string name, WmaIndex index, WmSyncReader reader)
    {
        _file = file;
        _name = name;
        _index = index;
        _reader = reader;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(reader.Format.SampleRate, reader.Format.Channels);
    }

    /// <summary>Opens <paramref name="file"/>, which the reader then owns, with its index kept
    /// in <paramref name="indexes"/>, or built now when there is none.</summary>
    public static WmaTrackReader Open(string path, Stream file, WmaIndexStore indexes, CancellationToken cancelled)
    {
        var name = Path.GetFileName(path);
        var index = indexes.For(path, file, cancelled);
        var reader = WmSyncReader.Open(file, name);
        if (reader.Format.BitsPerSample != 16)
        {
            reader.Dispose();
            throw new InvalidDataException($"Windows Media decoded {name} to {reader.Format.BitsPerSample}-bit, not 16.");
        }
        return new WmaTrackReader(file, name, index, reader);
    }

    public WaveFormat WaveFormat { get; }

    public long TotalFrames => _index.TotalFrames;

    private int BlockAlign => _reader.Format.BlockAlign;

    public long Position
    {
        get => _position;
        set => Seek(Compat.Clamp(value, 0, Math.Max(TotalFrames - 1, 0)));
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var channels = WaveFormat.Channels;
        var frames = 0;
        while (frames < count)
        {
            if (_pendingAt == _pendingBytes && !Fill())
            {
                break;
            }
            var n = Math.Min(count - frames, (_pendingBytes - _pendingAt) / BlockAlign);
            var samples = n * channels;
            var target = offset + frames * channels;
            for (var i = 0; i < samples; i++)
            {
                buffer[target + i] = BitConverter.ToInt16(_pending, _pendingAt + i * 2) / 32768f;
            }
            _pendingAt += n * BlockAlign;
            frames += n;
        }
        _position += frames;
        return frames;
    }

    /// <summary>The next decoded sample into the pending buffer; false at the end.</summary>
    private bool Fill()
    {
        if (!_reader.Next(ref _pending, out _, out var bytes))
        {
            _pendingAt = _pendingBytes = 0;
            return false;
        }
        _pendingAt = 0;
        _pendingBytes = bytes;
        return true;
    }

    private void Seek(long target)
    {
        if (!SeekByIndex(target))
        {
            SeeksFromTheStart++;
            FromTheStart(target);
        }
    }

    /// <summary>Seeks the index could not place, decoded forward from the start instead: past
    /// the first samples, a test's sign that the quick path was missed.</summary>
    internal int SeeksFromTheStart { get; private set; }

    /// <summary>Samples read after a seek looking for one the index names. A seek can split a
    /// sample of a decode from the start in two, and the one after is whole (measured).</summary>
    private const int SamplesToPlaceASeek = 8;

    /// <summary>Asks the runtime for the sample before the one <paramref name="target"/> is in,
    /// so it lands before the target however far off its stamps are. What it delivers is a
    /// decode from the start, sample for sample, but the first is cut wherever the runtime
    /// landed and a later one may be split in two. So the samples are placed by the first after
    /// the first that the index names: stamped as one of its samples, and ending where the next
    /// starts.</summary>
    private bool SeekByIndex(long target)
    {
        var at = _index.SampleAt(target) - 1;
        if (at < 1)
        {
            return false; // the first samples are as quick to reach from the start
        }
        _reader.SetRange(_index.Stamps[at]);
        _pendingAt = _pendingBytes = 0;
        long before = 0; // frames delivered before the sample in hand
        for (var read = 0; read < SamplesToPlaceASeek; read++)
        {
            if (!_reader.Next(ref _next, out var stamp, out var bytes))
            {
                return false;
            }
            var frames = bytes / BlockAlign;
            if (read > 0 && _index.SampleStamped(stamp) is { } named && _index.IsStart(_index.Starts[named] + frames))
            {
                var first = _index.Starts[named] - before;
                if (first > target)
                {
                    return false;
                }
                Append(bytes);
                _position = first;
                Discard(target - first);
                return true;
            }
            Append(bytes);
            before += frames;
        }
        return false;
    }

    /// <summary>Adds the sample in <see cref="_next"/> to what is pending.</summary>
    private void Append(int bytes)
    {
        if (_pending.Length < _pendingBytes + bytes)
        {
            Array.Resize(ref _pending, Math.Max(_pendingBytes + bytes, _pending.Length * 2));
        }
        Buffer.BlockCopy(_next, 0, _pending, _pendingBytes, bytes);
        _pendingBytes += bytes;
    }

    /// <summary>Decodes forward from the file's first sample, frame 0, to the target.</summary>
    private void FromTheStart(long target)
    {
        _reader.Dispose();
        _reader = WmSyncReader.Open(_file, _name);
        _pendingAt = _pendingBytes = 0;
        _position = 0;
        Discard(target);
    }

    private void Discard(long frames)
    {
        while (frames > 0)
        {
            if (_pendingAt == _pendingBytes && !Fill())
            {
                return;
            }
            var n = (int)Math.Min(frames, (_pendingBytes - _pendingAt) / BlockAlign);
            _pendingAt += n * BlockAlign;
            _position += n;
            frames -= n;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _reader.Dispose();
        _file.Dispose();
    }
}
