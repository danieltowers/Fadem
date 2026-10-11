using System.Globalization;
using Fadem.Core.Playback;
using NAudio.Flac;

namespace Fadem.Core.Tests;

/// <summary>A FLAC cut short (build issue 21). Before the fix, the decoder package read past its
/// buffer on the cut frame and the access violation killed whichever process it ran in, so every
/// decode of a cut file here runs in a child process (<see cref="ChildProcess"/>): a regression
/// fails one test instead of taking down the test host. The decode ends after the last whole
/// frame, and the engine tells the shortfall as ADR 0010's one notice.</summary>
public sealed class TruncatedFlacTests
{
    private const string Noise = "noise-44100-16-2.flac";

    // ── Where the decode ends ────────────────────────────────────────────────────────────

    public static IEnumerable<object[]> Fixtures =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"), "*.flac")
            .Select(path => Path.GetFileName(path))
            .Order()
            .Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EveryFixture_CutInsideAFrame_DecodesToTheFrameBefore_AndTheProcessLives(string fixture)
    {
        var frames = FrameTable(fixture);
        var k = frames.Count / 2;
        var path = Cut(fixture, Midway(frames, k));

        var child = ChildProcess.Run("decode", path);

        AssertLived(child);
        Assert.Equal(frames[k].SampleOffset, child["read"]);
        Assert.Equal(frames[k].SampleOffset, child["total"]);
        Assert.Equal(frames[k].SampleOffset, child["position"]);
    }

    [Fact]
    public void ACutInsideTheFinalFrame_DropsIt_ThoughTheFramesBeforeIt_CoverTheHeadersCount()
    {
        // The case a comparison with STREAMINFO's sample count would miss: every frame header
        // the pre-scan finds adds up to the full length. Only the final frame's CRC tells.
        var frames = FrameTable(Noise);
        var path = Cut(Noise, FileLength(Noise) - 3);

        var child = ChildProcess.Run("decode", path);

        AssertLived(child);
        Assert.Equal(frames[^1].SampleOffset, child["read"]);
    }

    [Fact]
    public void ACutOnAFrameBoundary_LosesNothing()
    {
        var frames = FrameTable(Noise);
        var k = frames.Count / 2;
        var path = Cut(Noise, frames[k].StreamOffset);

        var child = ChildProcess.Run("decode", path);

        AssertLived(child);
        Assert.Equal(frames[k].SampleOffset, child["read"]);
    }

    [Fact]
    public void ACutJustAfterAFrameHeader_DropsOnlyThatFrame()
    {
        // The fixture's headers are 6 bytes. The reader's stream reads zeros past the file's end,
        // so the pre-scan finds a whole header in its last 16 bytes (build issue 22).
        var frames = FrameTable(Noise);
        var k = frames.Count / 2;
        var path = Cut(Noise, frames[k].StreamOffset + 8);

        var child = ChildProcess.Run("decode", path);

        AssertLived(child);
        Assert.Equal(frames[k].SampleOffset, child["read"]);
    }

    [Fact]
    public void ACutInsideAFrameHeader_AlsoDropsTheWholeFrameBefore()
    {
        // A header cut short does not parse, so the stray start of frame k counts as the tail of
        // frame k - 1, whose CRC then fails at the file's end. One whole frame is lost; the file
        // was short anyway, and the notice is the same.
        var frames = FrameTable(Noise);
        var k = frames.Count / 2;
        var path = Cut(Noise, frames[k].StreamOffset + 3);

        var child = ChildProcess.Run("decode", path);

        AssertLived(child);
        Assert.Equal(frames[k - 1].SampleOffset, child["read"]);
    }

    [Fact]
    public void AFileWhoseTailIsZeros_DecodesToTheFrameBeforeTheZeros()
    {
        // A copy that stopped part way into a file already sized to full length.
        var frames = FrameTable(Noise);
        var k = frames.Count / 2;
        var bytes = File.ReadAllBytes(FixturePath(Noise));
        Array.Clear(bytes, (int)Midway(frames, k), bytes.Length - (int)Midway(frames, k));
        var path = Write(bytes);

        var child = ChildProcess.Run("decode", path);

        AssertLived(child);
        Assert.Equal(frames[k].SampleOffset, child["read"]);
    }

    [Fact]
    public void ACutInsideTheFirstFrame_IsAManagedErrorOnOpen()
    {
        // Nothing whole to play: the open fails, and the engine passes the row over as
        // Unreadable, as for any file that cannot be opened.
        var frames = FrameTable(Noise);
        var path = Cut(Noise, Midway(frames, 0));

        var child = ChildProcess.Run("decode", path);

        AssertLived(child);
        Assert.Equal("InvalidDataException", child.Values["error"]);
    }

    // ── Seeking in a cut file ────────────────────────────────────────────────────────────

    [Fact]
    public void ASeekIntoTheLastWholeFrame_LandsExactly_AndReadsOnToTheEnd()
    {
        var frames = FrameTable(Noise);
        var k = frames.Count / 2;
        var path = Cut(Noise, Midway(frames, k));
        var target = frames[k - 1].SampleOffset + 100;

        var child = ChildProcess.Run("decode", path, target.ToString(CultureInfo.InvariantCulture));

        AssertLived(child);
        Assert.Equal(target, child["landed"]);
        Assert.Equal(frames[k].SampleOffset - target, child["read"]);
        Assert.Equal(frames[k].SampleOffset, child["position"]);
    }

    [Fact]
    public void ASeekIntoTheDroppedFrame_StopsAtTheLastWholeSample()
    {
        var frames = FrameTable(Noise);
        var k = frames.Count / 2;
        var path = Cut(Noise, Midway(frames, k));

        var child = ChildProcess.Run("decode", path, (frames[k].SampleOffset + 100).ToString(CultureInfo.InvariantCulture));

        AssertLived(child);
        Assert.Equal(frames[k].SampleOffset - 1, child["landed"]);
        Assert.Equal(1, child["read"]);
    }

    // ── Whole files ──────────────────────────────────────────────────────────────────────

    // Safe in process: a whole file's last frame is whole, so even if the check refused it
    // the decoder would only stop a frame early, never meet a cut frame.

    [Fact]
    public void AWholeFileWithAnId3v1TagAfterItsLastFrame_DecodesInFull()
    {
        var tag = new byte[128];
        "TAG"u8.CopyTo(tag);

        AssertDecodesInFull([.. File.ReadAllBytes(FixturePath(Noise)), .. tag]);
    }

    [Fact]
    public void AWholeFileWithAnApeTagAndThenAnId3v1Tag_DecodesInFull()
    {
        var ape = ApeV2Tag(items: 40);
        var id3 = new byte[128];
        "TAG"u8.CopyTo(id3);

        AssertDecodesInFull([.. File.ReadAllBytes(FixturePath(Noise)), .. ape, .. id3]);
    }

    [Fact]
    public void AWholeFileWithOtherBytesAfterItsLastFrame_LosesThatFrame()
    {
        // The documented cost: bytes that are neither frames nor a known tag cannot be told
        // from the rest of a cut frame, so the last frame is refused.
        var frames = FrameTable(Noise);
        var path = Write([.. File.ReadAllBytes(FixturePath(Noise)), .. Enumerable.Repeat((byte)0x55, 100)]);

        using var source = TrackSources.Open(path);

        Assert.Equal(frames[^1].SampleOffset, source.TotalFrames);
        Assert.Equal(frames[^1].SampleOffset, TestAudio.ReadToEnd(source));
    }

    /// <summary>Opt-in, as it decodes every file: set <c>FADEM_MUSIC_FOLDER</c> to a folder of
    /// music. Every promised FLAC under it opens to exactly its header's length and decodes to
    /// it, so the whole-frame check takes no frame from a whole file (trailing bytes other than
    /// the tags it knows would make it), and a seek 1.5 s before the end lands on the sample a
    /// sequential decode has there. The header, not the package's own pre-scan: that loses a
    /// final frame under 16 bytes and invents frames, as 110 and 5 of the 245 files in the
    /// author's music folder showed (build issue 22). Whole files, so it runs in process. It
    /// decodes every file in full, so point it at a sample: 50 files took 3.5 minutes, and all
    /// 245 had not finished after hours. Without the variable it passes without looking.</summary>
    [Fact]
    public void EveryFlacInAMusicFolder_DecodesToItsHeadersLength_AndSeeksExactlyNearItsEnd()
    {
        var folder = Environment.GetEnvironmentVariable("FADEM_MUSIC_FOLDER");
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }
        var opened = 0;
        var wrong = new List<string>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.flac", SearchOption.AllDirectories))
        {
            if (AudioProbe.Probe(path, out var probe) is not null)
            {
                continue;
            }
            opened++;
            using var source = TrackSources.Open(path);
            var channels = source.WaveFormat.Channels;
            var tail = source.WaveFormat.SampleRate * 3 / 2;
            var window = 1024;
            if (source.TotalFrames != probe!.Frames)
            {
                wrong.Add($"{path}: header {probe.Frames}, opens to {source.TotalFrames}");
                continue;
            }
            var last = LastFrames(source, tail);
            if (source.Position != probe.Frames)
            {
                wrong.Add($"{path}: header {probe.Frames}, decodes to {source.Position}");
                continue;
            }
            source.Position = probe.Frames - tail;
            var landed = new float[window * channels];
            var read = 0;
            int n;
            while (read < window && (n = source.Read(landed, read * channels, window - read)) > 0)
            {
                read += n;
            }
            if (!landed.AsSpan().SequenceEqual(last.AsSpan(0, window * channels)))
            {
                wrong.Add($"{path}: a seek to {probe.Frames - tail} lands elsewhere");
            }
        }
        Assert.True(opened > 0, $"No promised FLAC under {folder}.");
        Assert.True(wrong.Count == 0,
            $"{wrong.Count} of {opened}:" + Environment.NewLine + string.Join(Environment.NewLine, wrong));
    }

    /// <summary>Reads <paramref name="source"/> to its end and returns its last
    /// <paramref name="frames"/> frames.</summary>
    private static float[] LastFrames(ITrackSource source, int frames)
    {
        var channels = source.WaveFormat.Channels;
        var ring = new float[frames * channels];
        var buffer = new float[4410 * channels];
        long total = 0;
        int n;
        while ((n = source.Read(buffer, 0, 4410)) > 0)
        {
            for (var i = 0; i < n * channels; i++)
            {
                ring[(total * channels + i) % ring.Length] = buffer[i];
            }
            total += n;
        }
        var start = (int)(total * channels % ring.Length);
        return [.. ring.AsSpan(start), .. ring.AsSpan(0, start)];
    }

    // ── Through the engine ───────────────────────────────────────────────────────────────

    [Fact]
    public void ACutFlacInTheQueue_PlaysToWhereItsWholeAudioEnds_WithOneNotice_AndTheNextTrackStartsThere()
    {
        var frames = FrameTable(Noise);
        var k = frames.Count / 2;
        var path = Cut(Noise, Midway(frames, k));

        var child = ChildProcess.Run("play", path);

        AssertLived(child);
        Assert.Equal(frames[k].SampleOffset, child["delivered"]);
        Assert.Equal(1, child["notices"]);
        Assert.Contains(Path.GetFileName(path), child.Values["notice0"]);
        Assert.Contains("before its header claims", child.Values["notice0"]);
        Assert.Equal(0.2, double.Parse(child.Values["after"], CultureInfo.InvariantCulture), 2);
        Assert.StartsWith("b-", Path.GetFileName(child.Values["current"])); // the DC WAV
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────

    private static void AssertLived(ChildProcess.Result child) =>
        Assert.True(child.ExitCode == 0, $"The child exited with {child.ExitCode}:\n{child.Output}");

    /// <summary>The fixture written as <paramref name="bytes"/> decodes to its full length.</summary>
    private static void AssertDecodesInFull(byte[] bytes)
    {
        var frames = FrameTable(Noise);
        var total = frames[^1].SampleOffset + frames[^1].Header.BlockSize;

        using var source = TrackSources.Open(Write(bytes));

        Assert.Equal(total, source.TotalFrames);
        Assert.Equal(total, TestAudio.ReadToEnd(source));
    }

    /// <summary>An APEv2 tag with a header and <paramref name="items"/> bytes of items: the
    /// 32-byte header and footer both start "APETAGEX" and give the size of items plus footer,
    /// with bit 31 of the flags saying a header is present.</summary>
    private static byte[] ApeV2Tag(int items)
    {
        byte[] Block()
        {
            var block = new byte[32];
            "APETAGEX"u8.CopyTo(block);
            BitConverter.GetBytes(2000).CopyTo(block, 8);
            BitConverter.GetBytes(items + 32).CopyTo(block, 12);
            BitConverter.GetBytes(1).CopyTo(block, 16);
            BitConverter.GetBytes(0x8000_0000u).CopyTo(block, 20);
            return block;
        }
        return [.. Block(), .. Enumerable.Repeat((byte)0x41, items), .. Block()];
    }

    private static string FixturePath(string fixture) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);

    private static long FileLength(string fixture) => new FileInfo(FixturePath(fixture)).Length;

    /// <summary>The byte halfway through frame <paramref name="k"/>.</summary>
    private static long Midway(IReadOnlyList<FlacFrameInformation> frames, int k) =>
        (frames[k].StreamOffset + frames[k + 1].StreamOffset) / 2;

    /// <summary>A copy of the fixture holding only its first <paramref name="bytes"/> bytes.</summary>
    private static string Cut(string fixture, long bytes) =>
        Write(File.ReadAllBytes(FixturePath(fixture))[..(int)bytes]);

    private static string Write(byte[] bytes)
    {
        Directory.CreateDirectory(TestAudio.Dir);
        var path = Path.Combine(TestAudio.Dir, $"cut-{Guid.NewGuid():N}.flac");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>The package's pre-scan of an intact file, a fixture by name or any file by its
    /// full path: where every frame starts, in bytes and in samples.</summary>
    private static IReadOnlyList<FlacFrameInformation> FrameTable(string fixture)
    {
        IReadOnlyList<FlacFrameInformation>? frames = null;
        using var stream = File.OpenRead(Path.IsPathRooted(fixture) ? fixture : FixturePath(fixture));
        using var reader = new FlacReader(stream, FlacPreScanMode.Sync, e => frames = e.Frames);
        return frames!;
    }
}
