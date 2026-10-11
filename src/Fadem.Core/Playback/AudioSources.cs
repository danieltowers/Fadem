using NAudio.Wave;

namespace Fadem.Core.Playback;

/// <summary>
/// A decodable, seekable stream of one audio file, in the file's own sample rate and channel
/// count. Sample positions are frames (one frame = one sample of every channel).
/// </summary>
internal interface ITrackSource : ISampleProvider, IDisposable
{
    long TotalFrames { get; }

    /// <summary>Current frame position within the track. Setting it seeks.</summary>
    long Position { get; set; }
}

/// <summary>
/// Opens audio files through the production decoders. Decoders are not a port (ADR 0003): one
/// adapter would be a hypothetical seam, and the real-input matrix uses real files. Errors are
/// thrown to the caller, which marks the row unreadable and passes over it with a notice.
/// </summary>
internal static class TrackSources
{
    /// <summary>The largest file held in memory when asked for: the compressed bytes, never the
    /// decoded samples. Up to three are held at once, during a transition: its two tracks and the
    /// one prepared after them. So a 32-bit process, the plugin's, shared with MusicBee
    /// (ADR 0012), holds smaller files.</summary>
    public static readonly long MemoryCap = (Environment.Is64BitProcess ? 256L : 64L) * 1024 * 1024;

    /// <summary>Opens the file to be read from disk as it plays.</summary>
    public static ITrackSource Open(string path) => Open(path, memoryCap: 0, CancellationToken.None);

    /// <summary>Opens the file, first reading it whole into memory when it is no larger than
    /// <paramref name="memoryCap"/>: it then plays without touching the disk again, and its file
    /// is not held open. A larger file, or one there is no memory for, is read from disk as it
    /// plays. A WMA's index is taken from <paramref name="wmaIndexes"/>, the engine's, or built
    /// now when it has none (issue 12).</summary>
    public static ITrackSource Open(
        string path, long memoryCap, CancellationToken cancelled, WmaIndexStore? wmaIndexes = null)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Audio file not found: {path}", path);
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        // Shared for delete: a track held open, the playing one or the next one opened when its
        // boundary was scheduled, never stops the user renaming or deleting its file.
        Stream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        try
        {
            file = HeldInMemory(file, memoryCap, cancelled);
            if (extension == ".wma")
            {
                // The Windows Media Format runtime, in both hosts; Media Foundation missed seeks by
                // up to 71 ms (ticket 02). Exact by its index (WmaTrackReader).
                return WmaTrackReader.Open(path, file, wmaIndexes ?? new WmaIndexStore(), cancelled);
            }
            WaveStream reader = extension switch
            {
                ".wav" => new WaveFileReader(file),
                // Trimmed to the encoded source when a valid LAME tag says how (issue 18).
                ".mp3" => Mp3TrackReader.Open(file),
                // The managed decoder (FlacTrackReader) lands seeks on the exact sample. Media
                // Foundation landed on the next block boundary, +67 ms on the ramp fixture, and
                // is out of the chain (ADR 0009).
                ".flac" => FlacTrackReader.Open(file),
                // The add-time probe rejects everything else before it gets here (ADR 0007).
                _ => throw new NotSupportedException(
                    $"Unsupported audio format '{extension}': not in docs/supported-audio.md."),
            };
            return WaveStreamTrackSource.Create(reader, file);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    private static Stream HeldInMemory(Stream file, long memoryCap, CancellationToken cancelled)
    {
        if (file.Length > memoryCap)
        {
            return file;
        }
        MemoryFile memory;
        try
        {
            memory = MemoryFile.ReadToEnd(file, cancelled);
        }
        catch (OutOfMemoryException)
        {
            file.Position = 0;
            return file;
        }
        file.Dispose();
        return memory;
    }
}

/// <summary>ITrackSource over any NAudio WaveStream (wav, mp3, flac).
/// Converts to IEEE float with correct *frame* semantics (count = frames, both channels filled) —
/// NAudio's built-in sample converters interpret count as per-stream samples, which breaks for
/// multi-channel sources.</summary>
internal sealed class WaveStreamTrackSource : ITrackSource
{
    private readonly WaveStream _stream;
    private readonly Stream _file;
    private byte[] _sourceBytes = new byte[0];
    private bool _disposed;

    private WaveStreamTrackSource(WaveStream stream, Stream file)
    {
        _stream = stream;
        _file = file;
    }

    /// <summary>Owns both the reader and the file under it: NAudio's readers given a stream
    /// leave it open.</summary>
    public static WaveStreamTrackSource Create(WaveStream stream, Stream file) => new(stream, file);

    public WaveFormat WaveFormat => _stream.WaveFormat;

    public long TotalFrames => _stream.Length / _stream.BlockAlign;

    public long Position
    {
        get => _stream.Position / _stream.BlockAlign;
        set
        {
            var clamped = Compat.Clamp(value, 0, Math.Max(TotalFrames - 1, 0));
            _stream.Position = clamped * _stream.BlockAlign;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var format = _stream.WaveFormat;
        var bytesRequired = count * format.BlockAlign;
        if (_sourceBytes.Length < bytesRequired)
        {
            _sourceBytes = new byte[bytesRequired];
        }
        var bytesRead = _stream.Read(_sourceBytes, 0, bytesRequired);
        var frames = bytesRead / format.BlockAlign;
        ConvertToFloat(Encoding(format), format.BitsPerSample, _sourceBytes, frames * format.Channels, buffer, offset);
        return frames;
    }

    /// <summary>The sample encoding to convert with. An extensible header — what ffmpeg and
    /// most tools write for 24- and 32-bit PCM — names its real encoding in a sub-format GUID;
    /// v2 had no case for it, so every block of an ordinary 24-bit WAV threw and the track
    /// rendered as silence (ticket 01, defect B).</summary>
    private static WaveFormatEncoding Encoding(WaveFormat format)
    {
        if (format.Encoding != WaveFormatEncoding.Extensible)
        {
            return format.Encoding;
        }
        // WaveFileReader hands back the raw chunk as WaveFormatExtraData: 2 bytes valid bits,
        // 4 bytes channel mask, then the 16-byte sub-format GUID. An in-process
        // WaveFormatExtensible exposes the GUID directly.
        var subFormat = format switch
        {
            WaveFormatExtensible extensible => extensible.SubFormat,
            WaveFormatExtraData extra when extra.ExtraData.Length >= 22 => Compat.GuidFrom(extra.ExtraData.AsSpan(6, 16)),
            _ => Guid.Empty,
        };
        return subFormat == SubtypeIeeeFloat ? WaveFormatEncoding.IeeeFloat : WaveFormatEncoding.Pcm;
    }

    /// <summary>KSDATAFORMAT_SUBTYPE_IEEE_FLOAT. Anything else in an extensible header is PCM.</summary>
    private static readonly Guid SubtypeIeeeFloat = new("00000003-0000-0010-8000-00aa00389b71");

    private static void ConvertToFloat(
        WaveFormatEncoding encoding, int bitsPerSample,
        byte[] source, int sampleCount, float[] target, int targetOffset)
    {
        switch (encoding)
        {
            case WaveFormatEncoding.IeeeFloat when bitsPerSample == 32:
                for (var i = 0; i < sampleCount; i++)
                {
                    target[targetOffset + i] = BitConverter.ToSingle(source, i * 4);
                }
                break;
            case WaveFormatEncoding.Pcm when bitsPerSample == 16:
                for (var i = 0; i < sampleCount; i++)
                {
                    target[targetOffset + i] = BitConverter.ToInt16(source, i * 2) / 32768f;
                }
                break;
            case WaveFormatEncoding.Pcm when bitsPerSample == 24:
                for (var i = 0; i < sampleCount; i++)
                {
                    var v = source[i * 3] | (source[i * 3 + 1] << 8) | (source[i * 3 + 2] << 16);
                    if ((v & 0x800000) != 0)
                    {
                        v |= unchecked((int)0xFF000000);
                    }
                    target[targetOffset + i] = v / 8388608f;
                }
                break;
            case WaveFormatEncoding.Pcm when bitsPerSample == 32:
                for (var i = 0; i < sampleCount; i++)
                {
                    target[targetOffset + i] = BitConverter.ToInt32(source, i * 4) / 2147483648f;
                }
                break;
            case WaveFormatEncoding.Pcm when bitsPerSample == 8:
                for (var i = 0; i < sampleCount; i++)
                {
                    target[targetOffset + i] = (source[i] - 128) / 128f;
                }
                break;
            default:
                throw new InvalidDataException(
                    $"Unsupported audio encoding: {encoding}, {bitsPerSample}-bit.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _stream.Dispose();
        _file.Dispose();
    }
}

