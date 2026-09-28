using System.Buffers.Binary;
using System.Collections.ObjectModel;
using NAudio.Flac;

namespace Faderio.Core.Playback;

/// <summary>
/// The production FLAC decoder: <c>BunLabs.NAudio.Flac</c>'s <see cref="FlacReader"/>, which
/// pre-scans every frame header on open and, on seek, lands on the frame before the target and
/// decodes-and-discards to the exact sample (ADR 0009). Five defects in 2.0.1 are repaired here
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
/// <item>A final frame shorter than 16 bytes is lost twice over: the pre-scan never looks for a
/// frame header in a stream's last 16 bytes, and the decoder reads 16 bytes for every header and
/// fails on fewer. A block of digital silence is such a frame, and 110 of 245 FLACs in the
/// author's music folder end in one, so each raised ADR 0010's notice at every play (build
/// issue 22).</item>
/// <item>The pre-scan takes any bytes that parse as a frame header of the stream's format for a
/// frame, and in 5 of those 245 files some bytes inside a frame did. The phantom frame adds its
/// block to <c>Length</c>, every seek after it lands late by that block, and a seek just after
/// it decodes it (build issue 22).</item>
/// </list>
/// This class counts frames itself and does its own sums over a table of the frames that are
/// really there (<see cref="RealFrames"/>), keeping the package's table only to ask its seek for
/// a frame by the offset it knows that frame by. It hands the package a stream that ends after
/// the last whole frame (<see cref="WholeFlacFrames"/>), so no cut frame ever reaches the
/// decoder: the track ends where its whole audio ends, which is ADR 0010's one notice. That
/// stream reads 16 bytes of zeros past its end, so that both the pre-scan and the header read
/// reach a short final frame. Everything else is the package's. The pre-scan runs synchronously
/// on open; its cost on a long file is measured in ticket 15 and is the reason the add-time
/// probe reads the header itself (ticket 12).
/// </summary>
internal sealed class FlacTrackReader : FlacReader
{
    /// <summary>The package's frame table, which its seek goes by.</summary>
    private readonly ReadOnlyCollection<FlacFrameInformation> _scanned;
    private readonly RealFrame[] _frames;
    private readonly int _wholeFlacFrames;
    private readonly long _length;
    private long _positionBytes;
    private bool _seeking;

    /// <summary>Reads the FLAC stream in <paramref name="stream"/>, which the caller owns.</summary>
    public static FlacTrackReader Open(Stream stream) => new(new EndingStream(stream), new ScanCapture());

    private FlacTrackReader(EndingStream stream, ScanCapture scan)
        : base(stream, FlacPreScanMode.Sync, scan.Capture)
    {
        _scanned = scan.Frames
            ?? throw new InvalidDataException("The FLAC pre-scan reported no frames.");
        if (_scanned.Count == 0)
        {
            throw new InvalidDataException("The FLAC stream holds no audio frames.");
        }
        _frames = RealFrames(_scanned);
        _wholeFlacFrames = WholeFlacFrames(stream.Inner, _frames);
        if (_wholeFlacFrames == 0)
        {
            throw new InvalidDataException("The FLAC stream is cut short inside its first audio frame.");
        }
        if (_wholeFlacFrames < _frames.Length)
        {
            stream.End = _frames[_wholeFlacFrames].StreamOffset;
            _length = _frames[_wholeFlacFrames].SampleOffset * BlockAlign;
        }
        else
        {
            _length = (_frames[^1].SampleOffset + _frames[^1].BlockSize) * BlockAlign;
        }
    }

    // The package's seek clamps its target to Length through this virtual getter, and it asks
    // for the frame by its own offsets, which a phantom frame pushes past ours.
    public override long Length => _seeking ? base.Length : _length;

    /// <summary>The frame table the pre-scan hands back, captured during the base constructor
    /// (a lambda there could not touch <c>this</c>).</summary>
    private sealed class ScanCapture
    {
        public ReadOnlyCollection<FlacFrameInformation>? Frames { get; private set; }

        public void Capture(FlacPreScanFinishedEventArgs e) => Frames = e.Frames;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        // Never on past the whole audio. At its end the package, finding no frame header, would
        // move on to the next frame in its table by a count that a phantom frame puts behind,
        // and play a frame again.
        if (!_seeking)
        {
            count = (int)Math.Min(count, _length - _positionBytes);
        }
        if (count <= 0)
        {
            return 0;
        }
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
            var target = Compat.Clamp(value - value % BlockAlign, 0, Length) / BlockAlign;
            var frame = FrameHolding(target);
            _seeking = true;
            try
            {
                if (frame == 0)
                {
                    base.Position = 0;
                    Discard(target * BlockAlign);
                }
                else
                {
                    // The package's seek lands on the frame before the first in its table that
                    // starts at or after the target. One sample into the frame before ours, by
                    // the package's own offset for it, that is the frame before ours: never a
                    // phantom, and never the package's last frame, whose seek does nothing. The
                    // package decodes that sample, and the rest is decoded and dropped here.
                    var before = _frames[frame - 1];
                    base.Position = (_scanned[before.Scanned].SampleOffset + 1) * BlockAlign;
                    Discard((target - before.SampleOffset - 1) * BlockAlign);
                }
            }
            finally
            {
                _seeking = false;
            }
            _positionBytes = target * BlockAlign;
        }
    }

    /// <summary>The whole frame that holds <paramref name="sample"/>, or the last whole frame for
    /// the end of the audio.</summary>
    private int FrameHolding(long sample)
    {
        var low = 0;
        var high = _wholeFlacFrames - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (_frames[middle].SampleOffset <= sample)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }
        return low;
    }

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

    /// <summary>A frame that is really in the stream: where it is in the package's table, where
    /// it starts in the file, and its first sample and length counted over real frames
    /// only.</summary>
    internal readonly record struct RealFrame(int Scanned, long StreamOffset, long SampleOffset, int BlockSize);

    /// <summary>
    /// The pre-scan's frames less the ones it made up. Each real frame is numbered on from the one
    /// before: by one frame, for a stream of fixed block size, or by the last frame's samples. The
    /// step is not read from the header's flag, as the package reads the number as a sample
    /// number whenever STREAMINFO's smallest and largest block sizes differ; either step passes.
    /// A frame out of step is a phantom when the frame after it is in step with the one before
    /// it, or when it is the last. Otherwise the numbering itself jumps (frames lost from the
    /// middle of a file, say), and the frame is kept: the decoder plays across the gap, and the
    /// shortfall is ADR 0010's notice. A phantom whose number happens to be in step passes.
    /// </summary>
    internal static RealFrame[] RealFrames(IReadOnlyList<FlacFrameInformation> scanned)
    {
        var frames = new List<RealFrame>(scanned.Count);
        long samples = 0;
        for (var i = 0; i < scanned.Count; i++)
        {
            if (frames.Count > 0)
            {
                var previous = scanned[frames[^1].Scanned].Header;
                if (!InStep(previous, scanned[i].Header)
                    && (i + 1 == scanned.Count || InStep(previous, scanned[i + 1].Header)))
                {
                    continue;
                }
            }
            frames.Add(new RealFrame(i, scanned[i].StreamOffset, samples, scanned[i].Header.BlockSize));
            samples += scanned[i].Header.BlockSize;
        }
        return [.. frames];
    }

    private static bool InStep(FlacFrameHeader previous, FlacFrameHeader next)
    {
        var step = Number(next) - Number(previous);
        return step == 1 || step == previous.BlockSize;
    }

    private static long Number(FlacFrameHeader header) =>
        header.BlockingStrategy == BlockingStrategy.FixedBlockSize ? header.FrameNumber : header.SampleNumber;

    /// <summary>
    /// How many of the real frames, from the first, are whole. A FLAC frame ends with a CRC-16 of
    /// everything before it, and a frame's bytes run to where the next real frame starts, or for
    /// the last one to where the audio ends (<see cref="AudioEnd"/>), so a phantom header inside
    /// a frame is only more of its bytes. Walking back from the last, the first whose bytes end
    /// with a matching CRC ends the whole audio. The CRC has no final XOR, so it is zero again
    /// after each whole frame: a frame the pre-scan missed still passes with the one before it.
    /// A truncated file fails only at its cut frame; a file whose tail is zeros where the copy
    /// never reached fails the same way, and a tail longer than any frame is refused without
    /// reading it. A cut frame passes by chance 1 time in 65536, at the one position checked. A
    /// whole last frame followed by anything but the tags <see cref="AudioEnd"/> knows
    /// (Lyrics3, say, or padding) is refused, and costs a notice and one frame of audio.
    /// </summary>
    internal static int WholeFlacFrames(Stream stream, IReadOnlyList<RealFrame> frames)
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

    /// <summary>The longest run of bytes that can be a whole frame: the longest frame FLAC can
    /// write, 65535 samples of 8 channels of 32 bits, verbatim, with a 16-byte header and the
    /// CRC-16. Not STREAMINFO's maximum frame size, which a header may understate.</summary>
    private const int LongestWholeRun = 65535 * 8 * 4 + 16 + 2;

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
    /// The stream the package decodes from: the file, read only up to <see cref="End"/>, and then
    /// 16 bytes of zeros. Until the pre-scan has run it is the whole file; then it ends after the
    /// last whole frame, so the decoder meets zeros where a cut frame would have started, finds
    /// no frame header there, and stops. The zeros are for a final frame shorter than 16 bytes:
    /// the pre-scan looks for a header only where 16 bytes follow, and the decoder reads 16 bytes
    /// for every header. A frame's body is read only to <see cref="Length"/>, so no frame ever
    /// decodes a zero. Seeks past the end are allowed and read nothing, as the package's read
    /// loop, on failing to find a header, jumps to the next frame it scanned.
    /// </summary>
    private sealed class EndingStream(Stream inner) : Stream
    {
        private const int Zeros = 16;

        public Stream Inner { get; } = inner;

        public long End { get; set; } = inner.Length;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => End;

        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var wanted = (int)Compat.Clamp(End + Zeros - Position, 0, count);
            var real = (int)Compat.Clamp(End - Position, 0, wanted);
            Inner.Position = Position;
            var read = Inner.ReadAtLeast(buffer.AsSpan(offset, real), real, throwOnEndOfStream: false);
            if (read < real)
            {
                // The file is shorter than when it was opened: it ends here.
                Position += read;
                return read;
            }
            Array.Clear(buffer, offset + real, wanted - real);
            Position += wanted;
            return wanted;
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
