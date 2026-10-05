using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Faderio.Core.Playback;

/// <summary>
/// Where every sample of one WMA file decodes to, from one full decode (issue 12). The Windows
/// Media Format runtime seeks in a millisecond or two anywhere in a file, but it stamps samples
/// in whole milliseconds, so a seek alone lands up to 2 ms from where its stamps say: past the
/// 1 ms promise (ADR 0009), and not to the sample (<c>WmaRuntimeTests</c>). A seek still
/// delivers the samples a decode from the start delivers, cut at the same boundaries, with
/// stamps within a millisecond of theirs. So a seek is placed by this index: the boundary a
/// seek's stamp names is a known frame. The decode also counts the length, exactly, codec tail
/// included (ADR 0010).
/// </summary>
internal sealed class WmaIndex
{
    private WmaIndex(long[] starts, long[] stamps, long totalFrames)
    {
        Starts = starts;
        Stamps = stamps;
        TotalFrames = totalFrames;
    }

    /// <summary>The frame each decoded sample starts at, ascending from 0.</summary>
    public long[] Starts { get; }

    /// <summary>Each sample's stamp, in the runtime's 100 ns units.</summary>
    public long[] Stamps { get; }

    /// <summary>Every frame a decode from the start delivers.</summary>
    public long TotalFrames { get; }

    public int Count => Starts.Length;

    /// <summary>The frames sample <paramref name="i"/> holds.</summary>
    public long Size(int i) => (i + 1 < Count ? Starts[i + 1] : TotalFrames) - Starts[i];

    /// <summary>The sample <paramref name="frame"/> falls in: the last that starts at or before it.</summary>
    public int SampleAt(long frame)
    {
        var i = Array.BinarySearch(Starts, frame);
        return i >= 0 ? i : Math.Max(0, ~i - 1);
    }

    /// <summary>Whether a sample starts at <paramref name="frame"/>, or the decode ends there.</summary>
    public bool IsStart(long frame) => frame == TotalFrames || Array.BinarySearch(Starts, frame) >= 0;

    /// <summary>A stamp a seek can be off by from the index's: a millisecond each way, measured,
    /// and a millisecond more. Samples are 30 ms and more apart, so a stamp names one.</summary>
    private const long StampSlack = 30_000;

    /// <summary>The sample a stamp delivered after a seek names, or null when none is near it.</summary>
    public int? SampleStamped(long stamp)
    {
        var i = Array.BinarySearch(Stamps, stamp);
        var nearest = i >= 0 ? i : NearerOf(~i - 1, ~i, stamp);
        return nearest >= 0 && Math.Abs(Stamps[nearest] - stamp) <= StampSlack ? nearest : null;
    }

    private int NearerOf(int before, int after, long stamp)
    {
        if (before < 0) return after < Count ? after : -1;
        if (after >= Count) return before;
        return stamp - Stamps[before] <= Stamps[after] - stamp ? before : after;
    }

    /// <summary>Decodes all of <paramref name="file"/> and records where each sample starts.</summary>
    public static WmaIndex Build(Stream file, string name, CancellationToken cancelled)
    {
        using var reader = WmSyncReader.Open(file, name);
        var starts = new List<long>();
        var stamps = new List<long>();
        var buffer = new byte[0];
        long frames = 0;
        while (reader.Next(ref buffer, out var stamp, out var bytes))
        {
            cancelled.ThrowIfCancellationRequested();
            starts.Add(frames);
            stamps.Add(stamp);
            frames += bytes / reader.Format.BlockAlign;
        }
        return new WmaIndex(starts.ToArray(), stamps.ToArray(), frames);
    }

    // ── On disk ──────────────────────────────────────────────────────────────────────────

    private static readonly byte[] Magic = "FWMI"u8.ToArray();
    private const int Version = 2;

    /// <summary>The index, after the audio it was built from, so other audio is built again.</summary>
    internal byte[] Serialise(AudioTail file)
    {
        var bytes = new byte[4 + 4 + 8 + 8 + 8 + 4 + Count * 12];
        var span = bytes.AsSpan();
        Magic.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], Version);
        file.Hash.CopyTo(span[8..24]);
        BinaryPrimitives.WriteInt64LittleEndian(span[24..], TotalFrames);
        BinaryPrimitives.WriteInt32LittleEndian(span[32..], Count);
        for (var i = 0; i < Count; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(span[(36 + i * 12)..], (int)Size(i));
            BinaryPrimitives.WriteInt64LittleEndian(span[(40 + i * 12)..], Stamps[i]);
        }
        return bytes;
    }

    /// <summary>The index in <paramref name="bytes"/> when it was built from
    /// <paramref name="file"/>'s audio; null for another version, other audio, or damage.</summary>
    internal static WmaIndex? Deserialise(byte[] bytes, AudioTail file)
    {
        var span = bytes.AsSpan();
        if (span.Length < 36 || !span[..4].SequenceEqual(Magic) ||
            BinaryPrimitives.ReadInt32LittleEndian(span[4..]) != Version ||
            !span[8..24].SequenceEqual(file.Hash))
        {
            return null;
        }
        var total = BinaryPrimitives.ReadInt64LittleEndian(span[24..]);
        var count = BinaryPrimitives.ReadInt32LittleEndian(span[32..]);
        if (count < 0 || span.Length != 36 + (long)count * 12)
        {
            return null;
        }
        var starts = new long[count];
        var stamps = new long[count];
        long frame = 0;
        for (var i = 0; i < count; i++)
        {
            starts[i] = frame;
            frame += BinaryPrimitives.ReadInt32LittleEndian(span[(36 + i * 12)..]);
            stamps[i] = BinaryPrimitives.ReadInt64LittleEndian(span[(40 + i * 12)..]);
        }
        return frame == total ? new WmaIndex(starts, stamps, total) : null;
    }
}

/// <summary>
/// What an index is built from: a hash of a WMA file's last mebibyte, its last audio packets. An
/// ASF file keeps its tags in its header, at the start, so a tag edit (routine in MusicBee)
/// changes the file's size and modified time but not this, and its index still holds; different
/// audio ends differently.
/// </summary>
internal sealed class AudioTail
{
    private const int Tail = 1 << 20;

    private AudioTail(byte[] hash) => Hash = hash;

    /// <summary>16 bytes of SHA-256.</summary>
    public byte[] Hash { get; }

    public bool Matches(AudioTail other) => Hash.AsSpan().SequenceEqual(other.Hash);

    /// <summary>Hashes the end of <paramref name="file"/>, and leaves its position at the start.</summary>
    public static AudioTail Of(Stream file)
    {
        var length = (int)Math.Min(Tail, file.Length);
        var bytes = new byte[length];
        file.Position = file.Length - length;
        var read = 0;
        int n;
        while (read < length && (n = file.Read(bytes, read, length - read)) > 0)
        {
            read += n;
        }
        file.Position = 0;
        using var sha = SHA256.Create();
        var hash = new byte[16];
        Array.Copy(sha.ComputeHash(bytes, 0, read), hash, 16);
        return new AudioTail(hash);
    }
}

/// <summary>
/// The WMA indexes one engine has built, kept so each file is decoded in full once: at add time,
/// when the probe counts its length (ADR 0010). Held in memory, and on disk in the library's
/// store when there is one, so a library opened again plays its WMA without decoding it again.
/// A file without an index is indexed when it is opened, a full decode: a file added on another
/// machine, or whose audio changed. Workers and the engine's lock holders
/// call it at once.
/// </summary>
internal sealed class WmaIndexStore
{
    private readonly ConcurrentDictionary<string, (AudioTail Audio, WmaIndex Index)> _memory =
        new(StringComparer.OrdinalIgnoreCase);

    private volatile string? _folder;

    /// <summary>Indexes this store has decoded a file to build, rather than found: a test's sign.</summary>
    internal int Built { get; private set; }

    /// <summary>The folder indexes are kept in, or null to keep them in memory only.</summary>
    public string? Folder
    {
        get => _folder;
        set => _folder = value;
    }

    /// <summary><paramref name="path"/>'s index: kept, or built from <paramref name="file"/> and kept.</summary>
    public WmaIndex For(string path, Stream file, CancellationToken cancelled)
    {
        var full = Path.GetFullPath(path);
        var audio = AudioTail.Of(file);
        if (_memory.TryGetValue(full, out var kept) && kept.Audio.Matches(audio))
        {
            return kept.Index;
        }
        // Read once: a library switched meanwhile never gets another library's index.
        var store = FileFor(full, _folder);
        var index = Load(store, audio);
        if (index is null)
        {
            index = Save(store, audio, WmaIndex.Build(file, Path.GetFileName(full), cancelled));
            Built++;
        }
        _memory[full] = (audio, index);
        return index;
    }

    private static WmaIndex? Load(string? file, AudioTail stamp)
    {
        if (file is null)
        {
            return null;
        }
        try
        {
            return File.Exists(file) ? WmaIndex.Deserialise(File.ReadAllBytes(file), stamp) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Writes the index beside the others, through a temporary file so a reader never
    /// sees half of one. A failure only costs the next open a decode.</summary>
    private static WmaIndex Save(string? file, AudioTail stamp, WmaIndex index)
    {
        if (file is null)
        {
            return index;
        }
        var temporary = $"{file}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(temporary, index.Serialise(stamp));
            FileSystemCompat.MoveReplacing(temporary, file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception again) when (again is IOException or UnauthorizedAccessException)
            {
                // Left behind; the index is still returned and kept in memory.
            }
        }
        return index;
    }

    /// <summary>One file per audio file, named by a hash of its full path.</summary>
    private static string? FileFor(string path, string? folder)
    {
        if (folder is null)
        {
            return null;
        }
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(path.ToUpperInvariant()));
        var name = BitConverter.ToString(hash, 0, 16).Replace("-", "").ToLowerInvariant();
        return Path.Combine(folder, name + ".wmaindex");
    }
}
