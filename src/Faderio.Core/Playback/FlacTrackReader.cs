using System.Buffers.Binary;
using System.Collections.ObjectModel;
using NAudio.Flac;

namespace Faderio.Core.Playback;

/// <summary>
/// The production FLAC decoder: <c>BunLabs.NAudio.Flac</c>'s <see cref="FlacReader"/>, which
/// pre-scans every frame header on open and, on seek, lands on the frame before the target and
/// decodes-and-discards to the exact sample (ADR 0009). Three defects in 2.0.1 are repaired here
/// rather than worked around in the wrapper, so that <see cref="ITrackSource.Position"/> stays
/// the decoder's word (ticket 15):
/// <list type="number">
/// <item>Its byte counter is skipped on any read that reaches the end of the stream, so after a
/// file is drained <c>Position</c> stops short of <c>Length</c> by the last partial read. The
/// 96 kHz fixture showed it: 192000 frames drained, position 189630.</item>
/// <item>Its seek walks the pre-scan for the first frame starting at or after the target, so a
/// target inside the last frame matches nothing and the seek is silently a no-op.</item>
/// <item>A frame cut short, as the last one of a truncated file is, kills the process: the
/// decoder reads the frame into a zero-filled buffer through unsafe code, and a Rice code's
/// unary run over the zeros past the cut runs off the buffer's end. The access violation is
/// not an exception .NET can catch (build issue 21).</item>
/// </list>
/// This class counts frames itself and seeks into the last frame by hand: to that frame's first
/// sample through the package's own exact seek, then forward by decoding. It hands the package
/// a stream that ends after the last whole frame (<see cref="WholeFlacFrames"/>), so no cut frame
/// ever reaches the decoder: the track ends where its whole audio ends, which is ADR 0010's one
/// notice. Everything else is the package's. The pre-scan runs synchronously on open; its cost
/// on a long file is measured in ticket 15 and is the reason the add-time probe reads the
/// header itself (ticket 12).
/// </summary>
internal sealed class FlacTrackReader : FlacReader
{
    private readonly ReadOnlyCollection<FlacFrameInformation> _frames;
    private readonly int _wholeFlacFrames;
    private readonly long _length;
    private long _positionBytes;

    /// <summary>Reads the FLAC stream in <paramref name="stream"/>, which the caller owns.</summary>
    public static FlacTrackReader Open(Stream stream) => new(new EndingStream(stream), new ScanCapture());

    private FlacTrackReader(EndingStream stream, ScanCapture scan)
        : base(stream, FlacPreScanMode.Sync, scan.Capture)
    {
        _frames = scan.Frames
            ?? throw new InvalidDataException("The FLAC pre-scan reported no frames.");
        if (_frames.Count == 0)
        {
            throw new InvalidDataException("The FLAC stream holds no audio frames.");
        }
        _wholeFlacFrames = WholeFlacFrames(stream.Inner, _frames);
        if (_wholeFlacFrames == 0)
        {
            throw new InvalidDataException("The FLAC stream is cut short inside its first audio frame.");
        }
        if (_wholeFlacFrames < _frames.Count)
        {
            stream.End = _frames[_wholeFlacFrames].StreamOffset;
            _length = _frames[_wholeFlacFrames].SampleOffset * BlockAlign;
        }
        else
        {
            _length = base.Length;
        }
    }

    public override long Length => _length;

    /// <summary>The frame table the pre-scan hands back, captured during the base constructor
    /// (a lambda there could not touch <c>this</c>).</summary>
    private sealed class ScanCapture
    {
        public ReadOnlyCollection<FlacFrameInformation>? Frames { get; private set; }

        public void Capture(FlacPreScanFinishedEventArgs e) => Frames = e.Frames;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = base.Read(buffer, offset, count);
        _positionBytes += read;
        return read;
    }

    public override long Position
    {
        // The package's seek reads Position back through this virtual getter to work out how
        // much to decode and discard, so while it seeks the getter must answer with the
        // package's own counter, not ours.
        get => _seeking ? base.Position : _positionBytes;
        set
        {
            var target = Math.Clamp(value - value % BlockAlign, 0, Length);
            var lastFrameStart = _frames[_wholeFlacFrames - 1].SampleOffset * BlockAlign;
            _seeking = true;
            try
            {
                if (target >= lastFrameStart)
                {
                    // The package's seek matches no frame here. Its seek to the last frame's
                    // own start is exact (it decodes the frame before and discards it), and
                    // from there the rest of the way is decoded and dropped.
                    base.Position = lastFrameStart;
                    Discard(target - lastFrameStart);
                }
                else
                {
                    base.Position = target;
                }
            }
            finally
            {
                _seeking = false;
            }
            _positionBytes = target;
        }
    }

    private bool _seeking;

    private void Discard(long bytes)
    {
        if (bytes <= 0)
        {
            return;
        }
        var chunk = 64 * 1024;
        chunk -= chunk % BlockAlign;
        var scratch = new byte[chunk];
        while (bytes > 0)
        {
            var wanted = (int)Math.Min(chunk, bytes);
            wanted -= wanted % BlockAlign;
            var read = base.Read(scratch, 0, wanted);
            if (read <= 0)
            {
                break;
            }
            bytes -= read;
        }
    }

    /// <summary>
    /// How many of the pre-scan's frames, from the first, are whole. A FLAC frame ends with a
    /// CRC-16 of everything before it, and a scanned frame's bytes run to where the next scanned
    /// frame starts, or for the last one to where the audio ends (<see cref="AudioEnd"/>).
    /// Walking back from the last, the first whose bytes end with a matching CRC ends the whole
    /// audio. The CRC has no final XOR, so it is zero again after each whole frame: frames the
    /// pre-scan missed after the last one it found (it never looks at a file's last 16 bytes)
    /// still pass with it, as whole frames, and the decoder's own reading decides what they
    /// deliver (build issue 22). A truncated file fails only at its cut frame; a file whose tail
    /// is zeros where the copy never reached fails the same way, and a tail longer than any
    /// frame is refused without reading it. A cut frame passes by chance 1 time in 65536, at
    /// the one position checked. A whole last frame followed by anything but whole frames and
    /// the tags <see cref="AudioEnd"/> knows (Lyrics3, say, or padding) is refused, and costs a
    /// notice and one frame of audio.
    /// </summary>
    internal static int WholeFlacFrames(Stream stream, IReadOnlyList<FlacFrameInformation> frames)
    {
        var saved = stream.Position;
        try
        {
            var audioEnd = AudioEnd(stream);
            for (var i = frames.Count - 1; i >= 0; i--)
            {
                var start = frames[i].StreamOffset;
                var end = Math.Min(i + 1 < frames.Count ? frames[i + 1].StreamOffset : audioEnd, audioEnd);
                if (EndsWithItsCrc(stream, start, end))
                {
                    return i + 1;
                }
            }
            return 0;
        }
        finally
        {
            stream.Position = saved;
        }
    }

    /// <summary>Where the audio ends: the end of the file, less the tags taggers append to a
    /// FLAC. An ID3v1 tag is the last 128 bytes, starting "TAG"; an APEv2 tag ends in a 32-byte
    /// footer, starting "APETAGEX", that gives its size (footer included, header not) and flags
    /// whether a 32-byte header precedes it. Either may follow the other.</summary>
    private static long AudioEnd(Stream stream)
    {
        var end = stream.Length;
        var footer = new byte[32];
        while (true)
        {
            if (end >= 128 && ReadAt(stream, end - 128, footer.AsSpan(0, 3)) && footer is [(byte)'T', (byte)'A', (byte)'G', ..])
            {
                end -= 128;
                continue;
            }
            if (end >= 32 && ReadAt(stream, end - 32, footer) && footer.AsSpan(0, 8).SequenceEqual("APETAGEX"u8))
            {
                var size = (long)BinaryPrimitives.ReadUInt32LittleEndian(footer.AsSpan(12));
                var hasHeader = (BinaryPrimitives.ReadUInt32LittleEndian(footer.AsSpan(20)) & 0x8000_0000) != 0;
                var tag = size + (hasHeader ? 32 : 0);
                if (size >= 32 && tag <= end)
                {
                    end -= tag;
                    continue;
                }
            }
            return end;
        }
    }

    private static bool ReadAt(Stream stream, long position, Span<byte> bytes)
    {
        stream.Position = position;
        return stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false) == bytes.Length;
    }

    /// <summary>The shortest frame: a 6-byte header, one byte per channel and the CRC-16.</summary>
    private const int ShortestFrame = 9;

    /// <summary>The longest run of bytes that can be whole frames: the longest frame FLAC can
    /// write (65535 samples of 8 channels of 32 bits, verbatim, a 16-byte header and the CRC-16),
    /// and the 16 bytes at the end of the file where the pre-scan finds no frame. Not
    /// STREAMINFO's maximum frame size, which a run of frames may exceed and a header may
    /// understate.</summary>
    private const int LongestWholeRun = 65535 * 8 * 4 + 16 + 2 + 16;

    private static bool EndsWithItsCrc(Stream stream, long start, long end)
    {
        var length = end - start;
        if (length < ShortestFrame || length > LongestWholeRun)
        {
            return false;
        }
        var bytes = new byte[length];
        if (!ReadAt(stream, start, bytes))
        {
            return false;
        }
        var stored = (ushort)(bytes[^2] << 8 | bytes[^1]);
        return Crc16(bytes.AsSpan(0, bytes.Length - 2)) == stored;
    }

    /// <summary>FLAC's frame CRC: polynomial x^16 + x^15 + x^2 + 1, initialised to zero, most
    /// significant bit first.</summary>
    private static ushort Crc16(ReadOnlySpan<byte> bytes)
    {
        ushort crc = 0;
        foreach (var b in bytes)
        {
            crc = (ushort)(crc << 8 ^ Crc16Table[(crc >> 8) ^ b]);
        }
        return crc;
    }

    private static readonly ushort[] Crc16Table = Enumerable.Range(0, 256).Select(n =>
    {
        var crc = (ushort)(n << 8);
        for (var bit = 0; bit < 8; bit++)
        {
            crc = (ushort)((crc & 0x8000) != 0 ? crc << 1 ^ 0x8005 : crc << 1);
        }
        return crc;
    }).ToArray();

    /// <summary>
    /// The stream the package decodes from: the file, read only up to <see cref="End"/>. Until
    /// the pre-scan has run it is the whole file; then it ends after the last whole frame, so
    /// the decoder meets the end of the stream where a cut frame would have started, finds no
    /// frame header there, and stops. Seeks past the end are allowed and read nothing, as the
    /// package's read loop, on failing to find a header, jumps to the next frame it scanned.
    /// </summary>
    private sealed class EndingStream(Stream inner) : Stream
    {
        public Stream Inner { get; } = inner;

        public long End { get; set; } = inner.Length;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => End;

        public override long Position
        {
            get => Inner.Position;
            set => Inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var left = End - Inner.Position;
            return left <= 0 ? 0 : Inner.Read(buffer, offset, (int)Math.Min(count, left));
        }

        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            _ => End + offset,
        };

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
