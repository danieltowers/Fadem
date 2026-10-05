using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Faderio.Core.Domain;

namespace Faderio.Core.Playback;

public enum AudioFormat
{
    Wav,
    Flac,
    Mp3,
    Wma,
}

/// <summary>The properties the add-time probe reads from a file's header. Together they pick a
/// row in <c>docs/supported-audio.md</c>, or fail to. <see cref="Codec"/> names a WMA's codec,
/// which the format alone does not (issue 12).</summary>
public sealed record AudioShape(AudioFormat Format, int SampleRate, int? BitDepth, int Channels, string? Codec = null)
{
    public override string ToString() =>
        (Codec ?? $"{Format}") + $" {SampleRate} Hz" + (BitDepth is { } b ? $" {b}-bit" : "") + $" {Channels} ch";
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
    Codec,
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
    private static readonly int[] WmaRates = { 44100, 48000 };
    private static readonly string[] Extensions = { ".wav", ".flac", ".mp3", ".wma" };

    /// <summary>The one WMA codec promised (issue 12): Pro, Lossless and Voice are unmeasured.</summary>
    internal const string WmaStandard = "WMA 9 Standard";

    /// <summary>Whether a file's extension names a promised format, in any case. Adding a folder
    /// takes only these and ignores the rest silently; a file named on its own is probed whatever
    /// its extension, so it is rejected with a reason instead.</summary>
    public static bool IsPromisedExtension(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

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
            case AudioFormat.Wma:
                if (shape.Codec != WmaStandard) return RejectedProperty.Codec;
                return WmaRates.Contains(shape.SampleRate) ? null : RejectedProperty.SampleRate;
            default:
                return RejectedProperty.Format;
        }
    }

    public static string Describe(RejectedProperty property, AudioShape? shape) => property switch
    {
        RejectedProperty.Channels => $"{shape?.Channels} channels is not promised; only mono and stereo are.",
        RejectedProperty.SampleRate => $"{shape?.SampleRate} Hz is not a promised rate for {shape?.Format}.",
        RejectedProperty.BitDepth => $"{shape?.BitDepth}-bit is not a promised depth; only 16 and 24-bit PCM are.",
        RejectedProperty.Codec => $"{shape?.Codec} is not a promised codec; only {WmaStandard} is.",
        RejectedProperty.Format => "Not a promised format; only WAV, FLAC, MP3 and WMA are.",
        _ => "The file could not be read.",
    };
}

/// <summary>
/// Reads a file's header to decide promised or rejected and to issue its length (ADR 0010). WAV:
/// the fmt and data chunks. FLAC: STREAMINFO. MP3: <see cref="Mp3Header"/>, whole frames less
/// the encoder's delay and padding when a valid LAME tag gives them. WMA, the one format whose
/// audio it reads: the Windows Media Format runtime's header, then, for a promised file, one full
/// decode, the <see cref="WmaIndex"/>, since no header gives the decoded length (issue 12). The
/// real-input matrix asserts on every promised cell that the length issued here equals what the
/// decoder delivers.
/// </summary>
internal static class AudioProbe
{
    internal static Rejection? Probe(string path, out ProbeResult? result, WmaIndexStore? wmaIndexes = null)
    {
        result = null;
        ProbeResult probed;
        try
        {
            if (!File.Exists(path))
            {
                return new Rejection(path, null, RejectedProperty.File, "The file does not exist.");
            }
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".wma")
            {
                return ProbeWma(path, wmaIndexes ?? new WmaIndexStore(), out result);
            }
            using var stream = File.OpenRead(path);
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

    // ── WMA ──────────────────────────────────────────────────────────────────────────────

    private static Rejection? ProbeWma(string path, WmaIndexStore indexes, out ProbeResult? result)
    {
        result = null;
        if (WmSyncReader.Unavailable is { } missing)
        {
            return new Rejection(path, null, RejectedProperty.Format, missing);
        }
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            AudioShape shape;
            using (var reader = WmSyncReader.Open(stream, Path.GetFileName(path)))
            {
                shape = new AudioShape(AudioFormat.Wma, reader.Format.SampleRate, null, reader.Format.Channels,
                    WmaCodec(reader.CodecTag));
            }
            if (SupportedAudio.Check(shape) is { } property)
            {
                return new Rejection(path, shape, property, SupportedAudio.Describe(property, shape));
            }
            result = new ProbeResult(shape, indexes.For(path, stream, CancellationToken.None).TotalFrames);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or COMException)
        {
            return new Rejection(path, null, RejectedProperty.File, e.Message);
        }
    }

    /// <summary>The codec a WMA stream's format tag names.</summary>
    private static string WmaCodec(ushort tag) => tag switch
    {
        0x0160 => "WMA 1",
        0x0161 => SupportedAudio.WmaStandard,
        0x0162 => "WMA 9 Pro",
        0x0163 => "WMA 9 Lossless",
        0x000A => "WMA 9 Voice",
        _ => $"WMA codec 0x{tag:X4}",
    };

    // ── MP3 ──────────────────────────────────────────────────────────────────────────────

    private static ProbeResult ProbeMp3(FileStream stream)
    {
        var header = Mp3Header.Read(stream);
        return new ProbeResult(header.Shape, header.Frames);
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
