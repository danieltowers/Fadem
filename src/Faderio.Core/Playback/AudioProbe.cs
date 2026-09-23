using System.Buffers.Binary;
using System.Text;
using Faderio.Core.Domain;

namespace Faderio.Core.Playback;

public enum AudioFormat
{
    Wav,
    Flac,
    Mp3,
}

/// <summary>The four properties the add-time probe reads from a file's header. Together they
/// pick a row in <c>docs/supported-audio.md</c>, or fail to.</summary>
public sealed record AudioShape(AudioFormat Format, int SampleRate, int? BitDepth, int Channels)
{
    public override string ToString() =>
        $"{Format} {SampleRate} Hz" + (BitDepth is { } b ? $" {b}-bit" : "") + $" {Channels} ch";
}

/// <summary>Which probed property disqualified a file. <see cref="File"/> means the header could
/// not be read at all (missing, unreadable, corrupt).</summary>
public enum RejectedProperty
{
    File,
    Format,
    SampleRate,
    BitDepth,
    Channels,
}

/// <summary>A file refused at add time, with the reason (ADR 0007). Structured so the shell
/// formats it and never has to parse a sentence.</summary>
public sealed record Rejection(string Path, AudioShape? Shape, RejectedProperty Property, string Detail)
{
    public override string ToString() =>
        $"{System.IO.Path.GetFileName(Path)}: {Detail}" + (Shape is null ? "" : $" ({Shape})");
}

/// <summary>What the probe learned: the shape and the length the header claims.</summary>
internal sealed record ProbeResult(AudioShape Shape, long Frames)
{
    public Length Length => Length.FromFrames(Frames, Shape.SampleRate);
}

/// <summary>
/// The promised table of <c>docs/supported-audio.md</c>, as code. A shape outside it is
/// rejected, naming the property that fell outside. The doc and this class change together.
/// </summary>
public static class SupportedAudio
{
    private static readonly int[] PcmRates = { 44100, 48000, 96000 };
    private static readonly int[] PcmDepths = { 16, 24 };
    private static readonly int[] Mp3Rates = { 44100, 48000 };

    public static RejectedProperty? Check(AudioShape shape)
    {
        if (shape.Channels is < 1 or > 2)
        {
            return RejectedProperty.Channels;
        }
        switch (shape.Format)
        {
            case AudioFormat.Wav:
            case AudioFormat.Flac:
                if (!PcmRates.Contains(shape.SampleRate)) return RejectedProperty.SampleRate;
                if (shape.BitDepth is not { } depth || !PcmDepths.Contains(depth)) return RejectedProperty.BitDepth;
                return null;
            case AudioFormat.Mp3:
                return Mp3Rates.Contains(shape.SampleRate) ? null : RejectedProperty.SampleRate;
            default:
                return RejectedProperty.Format;
        }
    }

    public static string Describe(RejectedProperty property, AudioShape? shape) => property switch
    {
        RejectedProperty.Channels => $"{shape?.Channels} channels is not promised; only mono and stereo are.",
        RejectedProperty.SampleRate => $"{shape?.SampleRate} Hz is not a promised rate for {shape?.Format}.",
        RejectedProperty.BitDepth => $"{shape?.BitDepth}-bit is not a promised depth; only 16 and 24-bit PCM are.",
        RejectedProperty.Format => "Not a promised format; only WAV, FLAC and MP3 are.",
        _ => "The file could not be read.",
    };
}

/// <summary>
/// Reads a file's header — never its audio — to decide promised or rejected and to issue its
/// length (ADR 0010). WAV: the fmt and data chunks. FLAC: STREAMINFO. MP3: the first frame
/// header plus the Xing/Info frame count, or the file size over the frame size for a bare
/// constant-bitrate file. The real-input matrix asserts on every promised cell that the length
/// issued here equals what the decoder delivers.
/// </summary>
internal static class AudioProbe
{
    internal static Rejection? Probe(string path, out ProbeResult? result)
    {
        result = null;
        ProbeResult probed;
        try
        {
            if (!File.Exists(path))
            {
                return new Rejection(path, null, RejectedProperty.File, "The file does not exist.");
            }
            using var stream = File.OpenRead(path);
            var extension = Path.GetExtension(path).ToLowerInvariant();
            var parsed = extension switch
            {
                ".wav" => ProbeWav(stream),
                ".flac" => ProbeFlac(stream),
                ".mp3" => ProbeMp3(stream),
                _ => null,
            };
            if (parsed is null)
            {
                return new Rejection(path, null, RejectedProperty.Format,
                    SupportedAudio.Describe(RejectedProperty.Format, null));
            }
            probed = parsed;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new Rejection(path, null, RejectedProperty.File, e.Message);
        }

        if (SupportedAudio.Check(probed.Shape) is { } property)
        {
            return new Rejection(path, probed.Shape, property, SupportedAudio.Describe(property, probed.Shape));
        }
        result = probed;
        return null;
    }

    // ── WAV ──────────────────────────────────────────────────────────────────────────────

    private static ProbeResult ProbeWav(FileStream stream)
    {
        Span<byte> head = stackalloc byte[12];
        ReadExactly(stream, head);
        if (!head[..4].SequenceEqual("RIFF"u8) || !head[8..12].SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("Not a RIFF/WAVE file.");
        }

        int? channels = null, rate = null, bits = null, formatTag = null;
        long? dataBytes = null;
        Span<byte> chunkHeader = stackalloc byte[8];
        while (dataBytes is null || channels is null)
        {
            if (stream.Read(chunkHeader) < 8)
            {
                break;
            }
            var id = Encoding.ASCII.GetString(chunkHeader[..4]);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..]);
            if (id == "fmt ")
            {
                var fmt = new byte[Math.Max(size, 16u)];
                ReadExactly(stream, fmt.AsSpan(0, (int)size));
                formatTag = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(0));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(2));
                rate = (int)BinaryPrimitives.ReadUInt32LittleEndian(fmt.AsSpan(4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(14));
                if (formatTag == 0xFFFE && size >= 40)
                {
                    formatTag = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(24)); // sub-format GUID's first word
                }
                if ((size & 1) == 1) stream.Seek(1, SeekOrigin.Current);
            }
            else if (id == "data")
            {
                dataBytes = size; // the header's claim, even when the file is shorter (ADR 0010)
                break;
            }
            else
            {
                stream.Seek(size + (size & 1), SeekOrigin.Current);
            }
        }
        if (channels is null || rate is null || bits is null || dataBytes is null)
        {
            throw new InvalidDataException("WAV header is missing its fmt or data chunk.");
        }
        // Only integer PCM is a promised depth; float reports its width so the rejection names it.
        var depth = formatTag == 1 ? bits.Value : formatTag == 3 ? 32 : 0;
        var blockAlign = Math.Max(1, channels.Value * (bits.Value / 8));
        return new ProbeResult(new AudioShape(AudioFormat.Wav, rate.Value, depth == 0 ? null : depth, channels.Value),
            dataBytes.Value / blockAlign);
    }

    // ── FLAC ─────────────────────────────────────────────────────────────────────────────

    private static ProbeResult ProbeFlac(FileStream stream)
    {
        Span<byte> marker = stackalloc byte[4];
        ReadExactly(stream, marker);
        if (!marker.SequenceEqual("fLaC"u8))
        {
            throw new InvalidDataException("Not a FLAC file.");
        }
        Span<byte> header = stackalloc byte[4];
        while (true)
        {
            ReadExactly(stream, header);
            var type = header[0] & 0x7F;
            var length = (header[1] << 16) | (header[2] << 8) | header[3];
            if (type == 0)
            {
                var info = new byte[length];
                ReadExactly(stream, info);
                // Bytes 10..17: 20 bits rate, 3 bits channels-1, 5 bits bps-1, 36 bits total samples.
                var rate = (info[10] << 12) | (info[11] << 4) | (info[12] >> 4);
                var channels = ((info[12] >> 1) & 0x7) + 1;
                var bps = (((info[12] & 0x1) << 4) | (info[13] >> 4)) + 1;
                var total = ((long)(info[13] & 0x0F) << 32) | ((long)info[14] << 24) | ((long)info[15] << 16) |
                            ((long)info[16] << 8) | info[17];
                return new ProbeResult(new AudioShape(AudioFormat.Flac, rate, bps, channels), total);
            }
            if ((header[0] & 0x80) != 0)
            {
                throw new InvalidDataException("FLAC file has no STREAMINFO block.");
            }
            stream.Seek(length, SeekOrigin.Current);
        }
    }

    // ── MP3 ──────────────────────────────────────────────────────────────────────────────

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

    private static ProbeResult ProbeMp3(FileStream stream)
    {
        var fileLength = stream.Length;
        long audioStart = 0;
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
            var shape = new AudioShape(AudioFormat.Mp3, frame.SampleRate, null, frame.Channels);
            var frames = XingFrames(window.AsSpan(i, Math.Min(frame.Length, got - i)), frame) is { } counted
                ? counted
                : (long)Math.Round((audioEnd - firstFrameOffset) / AverageFrameBytes(frame));
            return new ProbeResult(shape, frames * frame.SamplesPerFrame);
        }
        throw new InvalidDataException("No MP3 frame header found.");
    }

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

    /// <summary>The frame count from a Xing or Info header when the first frame carries one.</summary>
    private static long? XingFrames(ReadOnlySpan<byte> frame, Mp3Frame info)
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
        if ((flags & 1) == 0 || frame.Length < at + 12)
        {
            return null;
        }
        return BinaryPrimitives.ReadUInt32BigEndian(frame.Slice(at + 8));
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = stream.Read(buffer[read..]);
            if (n <= 0)
            {
                throw new InvalidDataException("Unexpected end of file in header.");
            }
            read += n;
        }
    }
}
