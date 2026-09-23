using NAudio.Wave;

namespace Faderio.Core.Playback;

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
/// thrown to the caller, which surfaces them as a notice and skips the track.
/// </summary>
internal static class TrackSources
{
    public static ITrackSource Open(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Audio file not found: {path}", path);
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".wav" => WaveStreamTrackSource.Create(new WaveFileReader(path)),
            ".mp3" => WaveStreamTrackSource.Create(new Mp3FileReader(path)),
            ".aif" or ".aiff" => WaveStreamTrackSource.Create(new AiffFileReader(path)),
            // FLAC decodes through Media Foundation until ticket 15 swaps the decoder (ADR 0009);
            // the catch-all goes with it. The probe rejects everything else before it gets here.
            ".flac" => WaveStreamTrackSource.Create(new MediaFoundationReader(path)),
            _ => WaveStreamTrackSource.Create(new MediaFoundationReader(path)),
        };
    }
}

/// <summary>ITrackSource over any NAudio WaveStream (wav, mp3, aiff, Media Foundation formats).
/// Converts to IEEE float with correct *frame* semantics (count = frames, both channels filled) —
/// NAudio's built-in sample converters interpret count as per-stream samples, which breaks for
/// multi-channel sources.</summary>
internal sealed class WaveStreamTrackSource : ITrackSource
{
    private readonly WaveStream _stream;
    private byte[] _sourceBytes = new byte[0];
    private bool _hasRead;
    private bool _disposed;

    private WaveStreamTrackSource(WaveStream stream) => _stream = stream;

    public static WaveStreamTrackSource Create(WaveStream stream) => new(stream);

    public WaveFormat WaveFormat => _stream.WaveFormat;

    public long TotalFrames => _stream.Length / _stream.BlockAlign;

    public long Position
    {
        get => _stream.Position / _stream.BlockAlign;
        set
        {
            // The Media Foundation reader silently drops a seek issued before its first read,
            // so an authored fade-in point on FLAC was discarded entirely (ticket 02). One
            // warm-up read makes the seek stick; the residual ~31 ms is ticket 09's business.
            if (!_hasRead && _stream is MediaFoundationReader)
            {
                var warmUp = new byte[_stream.BlockAlign * 4096]; // one full decode block
                _stream.Read(warmUp, 0, warmUp.Length);
                _hasRead = true;
            }
            var clamped = Math.Clamp(value, 0, Math.Max(TotalFrames - 1, 0));
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
        _hasRead = true;
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
            WaveFormatExtraData extra when extra.ExtraData.Length >= 22 => new Guid(extra.ExtraData.AsSpan(6, 16)),
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
    }
}

