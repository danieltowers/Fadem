using NAudio.Wave;

namespace Faderio.Core.Playback;

/// <summary>
/// The production MP3 decoder for a file with a valid LAME tag (issue 18): NAudio's
/// <see cref="Mp3FileReader"/> over Windows' ACM codec, trimmed to the encoded source. Position
/// <c>p</c> is the reader's <c>p + delay + D</c>, where <c>delay</c> is the encoder's (from the
/// tag) and <c>D</c> the decoder's, and the stream ends at <see cref="Mp3Header.Frames"/>. So
/// 0:00 is the first encoded sample and the length is the one the probe issued. This is offset
/// arithmetic over NAudio's seek, which is already exact to the sample (ticket 14); nothing is
/// decoded differently. A file without a valid tag gets the plain reader: whole frames.
/// </summary>
internal sealed class Mp3TrackReader : WaveStream
{
    /// <summary>D: where the ACM decoder puts the source's first sample after the encoder's
    /// delay. 529, as for standard decoders, measured and pinned by Mp3DecoderOffsetTests.</summary>
    internal const int DecoderDelay = 529;

    private readonly Mp3FileReader _reader;
    private readonly long _skip;
    private readonly long _frames;
    private readonly long _silentFrom;
    private long _position;

    /// <summary>Reads the MP3 stream in <paramref name="stream"/>, which the caller owns.</summary>
    public static WaveStream Open(Stream stream)
    {
        var header = Mp3Header.Read(stream);
        stream.Seek(0, SeekOrigin.Begin);
        var reader = new Mp3FileReader(stream);
        if (header.Gapless is null)
        {
            return reader;
        }
        try
        {
            return new Mp3TrackReader(reader, header);
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    private Mp3TrackReader(Mp3FileReader reader, Mp3Header header)
    {
        _reader = reader;
        _skip = header.Gapless!.Value.Delay + DecoderDelay;
        _frames = header.Frames;
        _silentFrom = header.WholeFrames - _skip;
        Seek(0);
    }

    public override WaveFormat WaveFormat => _reader.WaveFormat;

    public override long Length => _frames * BlockAlign;

    public override long Position
    {
        get => _position * BlockAlign;
        set => Seek(Compat.Clamp(value / BlockAlign, 0, _frames));
    }

    private long ReaderFrames => _reader.Length / BlockAlign;

    private void Seek(long frame)
    {
        _reader.Position = Math.Min(frame + _skip, ReaderFrames) * BlockAlign;
        _position = frame;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var frames = Math.Min(count / BlockAlign, _frames - _position);
        // Past the decoder's last whole frame, the source's final samples are silence: the tag's
        // padding was less than D, so the decoder's delay carried them beyond the last frame,
        // where no decoder reaches. LAME always pads by 576 or more; this is for other encoders,
        // and keeps the length the one the probe issued. A decoder that stops before its last
        // frame (a cut file) is not padded: the track ends there, with its notice (ADR 0010).
        var decodable = Compat.Clamp(_silentFrom - _position, 0, frames);
        var wanted = (int)decodable * BlockAlign;
        var read = 0;
        while (read < wanted)
        {
            var n = _reader.Read(buffer, offset + read, wanted - read);
            if (n <= 0)
            {
                break;
            }
            read += n;
        }
        if (read == wanted && frames > decodable)
        {
            var silence = (int)(frames - decodable) * BlockAlign;
            Array.Clear(buffer, offset + read, silence);
            read += silence;
        }
        _position += read / BlockAlign;
        return read;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _reader.Dispose();
        }
        base.Dispose(disposing);
    }
}
