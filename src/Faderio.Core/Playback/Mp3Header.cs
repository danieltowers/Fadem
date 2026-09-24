using System.Buffers.Binary;

namespace Faderio.Core.Playback;

/// <summary>The encoder's delay and padding, in samples, from a LAME tag that passed all three
/// checks (issue 18): the samples it added before the source and after it.</summary>
internal readonly record struct Mp3Gapless(int Delay, int Padding);

/// <summary>
/// What an MP3's header says, read without decoding: the first frame header, then the Xing/Info
/// frame count and its LAME tag when the first frame carries one, or the file size over the
/// frame size for a bare constant-bitrate file. The add-time probe issues <see cref="Frames"/>
/// as the track's length (ADR 0010) and <see cref="Mp3TrackReader"/> trims the decode to it,
/// each reading the header the same way.
/// </summary>
internal sealed record Mp3Header(AudioShape Shape, long WholeFrames, Mp3Gapless? Gapless)
{
    // WholeFrames counts audio frames (samples of every channel), like every Frames in the
    // engine: the MP3 frames times 1152. MP3 frames are called mp3Frames where they appear.

    /// <summary>The length the probe issues: whole frames, less the encoder's delay and padding
    /// when a valid LAME tag gives them. That is the encoded source's own sample count, and
    /// 0:00 is its first sample.</summary>
    public long Frames => Gapless is { } g ? WholeFrames - g.Delay - g.Padding : WholeFrames;

    private static readonly int[][] BitrateTable =
    {
        // MPEG-1 Layer III, kbit/s, index 1..14
        new[] { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 },
        // MPEG-2 / 2.5 Layer III
        new[] { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0 },
    };

    private static readonly int[][] RateTable =
    {
        new[] { 44100, 48000, 32000, 0 }, // MPEG-1
        new[] { 22050, 24000, 16000, 0 }, // MPEG-2
        new[] { 11025, 12000, 8000, 0 },  // MPEG-2.5
    };

    private sealed record Mp3Frame(int Version, int SampleRate, int Channels, int Bitrate, int Length, int SamplesPerFrame, bool Mono);

    /// <summary>The Xing/Info frame's frame count, if it has one, and its LAME extension, if it
    /// has one, unvalidated.</summary>
    private sealed record XingTag(long? Frames, LameTag? Lame);

    private sealed record LameTag(int Delay, int Padding, long MusicLength, bool CrcMatches);

    /// <summary>Reads from the start of <paramref name="stream"/> and leaves it anywhere.</summary>
    public static Mp3Header Read(Stream stream)
    {
        var fileLength = stream.Length;
        long audioStart = 0;
        stream.Seek(0, SeekOrigin.Begin);
        Span<byte> id3 = stackalloc byte[10];
        if (stream.Read(id3) == 10 && id3[..3].SequenceEqual("ID3"u8))
        {
            var size = (id3[6] << 21) | (id3[7] << 14) | (id3[8] << 7) | id3[9];
            audioStart = 10 + size + ((id3[5] & 0x10) != 0 ? 10 : 0);
        }
        long audioEnd = fileLength;
        if (fileLength >= 128)
        {
            stream.Seek(fileLength - 128, SeekOrigin.Begin);
            Span<byte> tag = stackalloc byte[3];
            if (stream.Read(tag) == 3 && tag.SequenceEqual("TAG"u8))
            {
                audioEnd -= 128;
            }
        }

        // Find the first frame header that is followed by another valid header.
        stream.Seek(audioStart, SeekOrigin.Begin);
        var window = new byte[Math.Min(64 * 1024, Math.Max(0, audioEnd - audioStart))];
        var got = stream.Read(window, 0, window.Length);
        for (var i = 0; i + 4 <= got; i++)
        {
            var frame = ParseFrame(window.AsSpan(i));
            if (frame is null)
            {
                continue;
            }
            var nextAt = i + frame.Length;
            if (nextAt + 4 <= got && ParseFrame(window.AsSpan(nextAt)) is null)
            {
                continue; // a false sync
            }
            var firstFrameOffset = audioStart + i;
            var audioBytes = audioEnd - firstFrameOffset;
            var shape = new AudioShape(AudioFormat.Mp3, frame.SampleRate, null, frame.Channels);
            var xing = ReadXing(window.AsSpan(i, Math.Min(frame.Length, got - i)), frame);
            var mp3Frames = xing?.Frames ?? (long)Math.Round(audioBytes / AverageFrameBytes(frame));
            var wholeFrames = mp3Frames * frame.SamplesPerFrame;
            var gapless = xing?.Lame is { } lame && IsValid(lame, wholeFrames, audioBytes)
                ? new Mp3Gapless(lame.Delay, lame.Padding)
                : (Mp3Gapless?)null;
            return new Mp3Header(shape, wholeFrames, gapless);
        }
        throw new InvalidDataException("No MP3 frame header found.");
    }

    /// <summary>The three checks of issue 18. There is no encoder allowlist: a passing CRC already
    /// proves LAME's layout. A tag that fails any of them is ignored silently and the file keeps
    /// whole frames, as a bare file does.</summary>
    private static bool IsValid(LameTag lame, long wholeFrames, long audioBytes) =>
        lame.CrcMatches
        // Catches a file cut or extended after encoding (Rockbox's allowance).
        && Math.Abs(lame.MusicLength - audioBytes) <= Math.Max(10 * 1024, audioBytes / 20)
        && lame.Delay <= 2880 && lame.Padding <= 2304
        && lame.Delay + lame.Padding < wholeFrames;

    private static Mp3Frame? ParseFrame(ReadOnlySpan<byte> b)
    {
        if (b.Length < 4 || b[0] != 0xFF || (b[1] & 0xE0) != 0xE0)
        {
            return null;
        }
        var versionBits = (b[1] >> 3) & 0x3; // 0: 2.5, 1: reserved, 2: MPEG-2, 3: MPEG-1
        var layerBits = (b[1] >> 1) & 0x3;   // 1: layer III
        if (versionBits == 1 || layerBits != 1)
        {
            return null;
        }
        var bitrateIndex = b[2] >> 4;
        var rateIndex = (b[2] >> 2) & 0x3;
        var padding = (b[2] >> 1) & 0x1;
        var channelMode = b[3] >> 6;
        if (bitrateIndex is 0 or 15 || rateIndex == 3)
        {
            return null;
        }
        var version = versionBits == 3 ? 0 : versionBits == 2 ? 1 : 2;
        var bitrate = BitrateTable[version == 0 ? 0 : 1][bitrateIndex] * 1000;
        var rate = RateTable[version][rateIndex];
        var samplesPerFrame = version == 0 ? 1152 : 576;
        var length = samplesPerFrame / 8 * bitrate / rate + padding;
        return new Mp3Frame(version, rate, channelMode == 3 ? 1 : 2, bitrate, length, samplesPerFrame, channelMode == 3);
    }

    private static double AverageFrameBytes(Mp3Frame frame) =>
        frame.SamplesPerFrame / 8.0 * frame.Bitrate / frame.SampleRate;

    /// <summary>The Xing or Info header, when the first frame carries one. The LAME extension
    /// follows whichever optional Xing fields the flags say are present (Rockbox's walk), and
    /// its layout is the Info Tag rev 1 spec's: the encoder delay and padding as two 12-bit
    /// values at +21, MusicLength at +28 and the tag CRC at +34. Trimming needs the tag's own
    /// frame count, so a LAME extension without one is not read.</summary>
    private static XingTag? ReadXing(ReadOnlySpan<byte> frame, Mp3Frame info)
    {
        var sideInfo = info.Version == 0 ? (info.Mono ? 17 : 32) : (info.Mono ? 9 : 17);
        var at = 4 + sideInfo;
        if (frame.Length < at + 8)
        {
            return null;
        }
        var tag = frame.Slice(at, 4);
        if (!tag.SequenceEqual("Xing"u8) && !tag.SequenceEqual("Info"u8))
        {
            return null;
        }
        var flags = BinaryPrimitives.ReadInt32BigEndian(frame.Slice(at + 4));
        var field = at + 8;
        long? frames = null;
        if ((flags & 1) != 0)
        {
            if (frame.Length < field + 4)
            {
                return null;
            }
            frames = BinaryPrimitives.ReadUInt32BigEndian(frame.Slice(field));
            field += 4;
        }
        if ((flags & 2) != 0) field += 4;   // bytes
        if ((flags & 4) != 0) field += 100; // table of contents
        if ((flags & 8) != 0) field += 4;   // quality
        const int LameLength = 36;
        if (frames is null || frame.Length < field + LameLength)
        {
            return new XingTag(frames, null);
        }
        var lame = frame.Slice(field, LameLength);
        var delay = (lame[21] << 4) | (lame[22] >> 4);
        var padding = ((lame[22] & 0xF) << 8) | lame[23];
        var musicLength = BinaryPrimitives.ReadUInt32BigEndian(lame[28..]);
        var crc = BinaryPrimitives.ReadUInt16BigEndian(lame[34..]);
        return new XingTag(frames, new LameTag(delay, padding, musicLength, CrcMatches(frame, field + 34, crc)));
    }

    /// <summary>The spec defines the tag CRC over "the first 190 bytes" of the frame, which is
    /// where the CRC sits in its stereo example. LAME covers the frame up to the CRC. ffmpeg's
    /// muxer covers 190 bytes of its frame buffer, taken while the CRC field is still zero, and
    /// zero past the end of a frame shorter than that. For stereo those are the same bytes. For
    /// mono the CRC sits at 175 and they differ (every mono fixture is ffmpeg's; the 44.1 kHz
    /// VBR ones have a 182-byte frame), so either passes.</summary>
    private static bool CrcMatches(ReadOnlySpan<byte> frame, int crcAt, ushort stored)
    {
        const int SpecLength = 190;
        if (Crc16(frame[..crcAt]) == stored)
        {
            return true;
        }
        if (crcAt + 2 > SpecLength)
        {
            return false;
        }
        Span<byte> covered = stackalloc byte[SpecLength];
        covered.Clear();
        frame[..Math.Min(frame.Length, SpecLength)].CopyTo(covered);
        covered[crcAt] = 0;
        covered[crcAt + 1] = 0;
        return Crc16(covered) == stored;
    }

    /// <summary>CRC-16/ARC (polynomial 0x8005 reflected, initial 0), LAME's tag CRC.</summary>
    private static ushort Crc16(ReadOnlySpan<byte> bytes)
    {
        ushort crc = 0;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
            }
        }
        return crc;
    }
}
