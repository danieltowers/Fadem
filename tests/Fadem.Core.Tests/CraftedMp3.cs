using System.Buffers.Binary;
using System.Text;

namespace Fadem.Core.Tests;

/// <summary>
/// Byte surgery on the LAME tag of a checked-in MP3 fixture (issue 18). <c>lame.exe</c> is not
/// available to <c>Fixtures/generate.sh</c>, and ffmpeg writes only <c>Lavc</c> tags, so the
/// cells a library file would need (a <c>LAME</c> encoder string, a tag that fails each check)
/// are crafted here from ffmpeg's fixtures, deterministically and without ffmpeg. The audio is
/// untouched. Each copy lands in <see cref="TestAudio.Dir"/>.
/// </summary>
internal static class CraftedMp3
{
    /// <summary>The fixture with another encoder string, re-signed so the tag CRC still passes.
    /// This is what <c>lame.exe</c> writes; it is otherwise the same layout.</summary>
    public static string WithEncoder(string fixture, string encoder) =>
        Craft(fixture, "enc", (bytes, tag) =>
        {
            var name = new byte[9];
            Encoding.ASCII.GetBytes(encoder).AsSpan(0, Math.Min(9, encoder.Length)).CopyTo(name);
            name.CopyTo(bytes, tag.Lame);
            Resign(bytes, tag);
        });

    /// <summary>The fixture with its stored tag CRC flipped: every value intact, the tag failing.</summary>
    public static string WithBrokenTagCrc(string fixture) =>
        Craft(fixture, "crc", (bytes, tag) =>
        {
            bytes[tag.Crc] ^= 0xFF;
            bytes[tag.Crc + 1] ^= 0xFF;
        });

    /// <summary>The fixture claiming another MusicLength, re-signed: as if the file had been cut
    /// or extended after encoding.</summary>
    public static string WithMusicLength(string fixture, uint musicLength) =>
        Craft(fixture, "len", (bytes, tag) =>
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(tag.Lame + 28), musicLength);
            Resign(bytes, tag);
        });

    /// <summary>The fixture claiming another delay and padding, re-signed.</summary>
    public static string WithDelayAndPadding(string fixture, int delay, int padding) =>
        Craft(fixture, "dly", (bytes, tag) =>
        {
            bytes[tag.Lame + 21] = (byte)(delay >> 4);
            bytes[tag.Lame + 22] = (byte)(((delay & 0xF) << 4) | (padding >> 8));
            bytes[tag.Lame + 23] = (byte)padding;
            Resign(bytes, tag);
        });

    /// <summary>The fixture with another Xing/Info frame count, re-signed.</summary>
    public static string WithFrameCount(string fixture, uint frames) =>
        Craft(fixture, "frm", (bytes, tag) =>
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(tag.Xing + 8), frames);
            Resign(bytes, tag);
        });

    /// <summary>The MusicLength the fixture's tag claims.</summary>
    public static uint MusicLength(string fixture)
    {
        var bytes = File.ReadAllBytes(fixture);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(Locate(bytes).Lame + 28));
    }

    /// <summary>Where the tag's parts sit in the file: the first frame, its Xing/Info tag, the
    /// LAME extension after the optional Xing fields, and its CRC 34 bytes in.</summary>
    private readonly record struct TagAt(int Frame, int Xing, int Lame)
    {
        public int Crc => Lame + 34;
    }

    private static string Craft(string fixture, string what, Action<byte[], TagAt> edit)
    {
        var bytes = File.ReadAllBytes(fixture);
        edit(bytes, Locate(bytes));
        Directory.CreateDirectory(TestAudio.Dir);
        var path = Path.Combine(TestAudio.Dir,
            $"{Path.GetFileNameWithoutExtension(fixture)}-{what}-{Guid.NewGuid():N}.mp3");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static TagAt Locate(byte[] bytes)
    {
        var at = 0;
        if (bytes.AsSpan(0, 3).SequenceEqual("ID3"u8))
        {
            at = 10 + ((bytes[6] << 21) | (bytes[7] << 14) | (bytes[8] << 7) | bytes[9]);
        }
        while (!(bytes[at] == 0xFF && (bytes[at + 1] & 0xE0) == 0xE0))
        {
            at++;
        }
        var mono = bytes[at + 3] >> 6 == 3;
        var xing = at + 4 + (mono ? 17 : 32); // MPEG-1 side information
        var tag = bytes.AsSpan(xing, 4);
        if (!tag.SequenceEqual("Info"u8) && !tag.SequenceEqual("Xing"u8))
        {
            throw new InvalidOperationException("The fixture has no Xing/Info frame to craft.");
        }
        var flags = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(xing + 4));
        var lame = xing + 8 + ((flags & 1) != 0 ? 4 : 0) + ((flags & 2) != 0 ? 4 : 0) +
                   ((flags & 4) != 0 ? 100 : 0) + ((flags & 8) != 0 ? 4 : 0);
        if ((flags & 1) == 0)
        {
            throw new InvalidOperationException("The fixture's Xing/Info frame has no frame count.");
        }
        return new TagAt(at, xing, lame);
    }

    /// <summary>CRC-16/ARC over the frame up to the tag CRC, as LAME writes it. ffmpeg's own
    /// fixtures differ for mono (Mp3Header.CrcMatches), so a crafted mono tag also proves LAME's
    /// convention passes.</summary>
    private static void Resign(byte[] bytes, TagAt tag)
    {
        ushort crc = 0;
        for (var i = tag.Frame; i < tag.Crc; i++)
        {
            crc ^= bytes[i];
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
            }
        }
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(tag.Crc), crc);
    }
}
