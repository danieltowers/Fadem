using Faderio.Core.Playback;

namespace Faderio.Core.Tests;

/// <summary>
/// Pins how MusicBee's own BASS decodes and seeks WMA (research, 2026-10-05,
/// <c>docs/research/wma-through-musicbee-bass.md</c>). WMA is rejected (ADR 0007): Media
/// Foundation missed seeks by up to 71 ms (ticket 02). In the plugin there is a second decoder,
/// the BASS that MusicBee hands a plugin through <c>Player_OpenStreamHandle</c>, and its
/// <c>basswma.dll</c> wraps the Windows Media Format runtime, not Media Foundation. These
/// measure it on MusicBee's DLLs (<see cref="MusicBeeBass"/>) by the matrix's methods: a
/// seek's landing is the first block read after it, located in a sequential decode of the same
/// pink noise, and the source is the lossless FLAC fixture of that noise.
/// <para>
/// Measured on BASS 2.4.17.0 and basswma 2.4.5, the DLLs of MusicBee 3.6:
/// <list type="bullet">
/// <item>BASS's own seek, a player's, is not exact. On files from Windows' encoder (what Windows
/// Media Player rips) it lands late by 0.1 to 1.6 ms, beyond 1 ms on about one seek in five; on
/// ffmpeg's it lands about one 2048-sample block early, 45 ms.</item>
/// <item><c>BASS_POS_DECODETO</c> from the start of a fresh stream lands on the sample, on every
/// file of both encoders.</item>
/// <item>The decode starts at the source's first sample on both. Windows' files end in up to
/// 43 ms of padding past the source, and BASS's length is the padded length, within 1 ms (16
/// frames short at 44.1 kHz). ffmpeg's lose the source's last partial block, and BASS's length is
/// not what it delivers.</item>
/// </list>
/// </para>
/// If one of these goes red, MusicBee's BASS has changed: measure again, do not re-pin by
/// guessing. net48 x86 only: MusicBee's DLLs are 32-bit (excluded from net8.0 in the project).
/// </summary>
public sealed class MusicBeeBassWmaTests
{
    /// <summary>BASS_CTYPE_STREAM_WMA: basswma decoded it, not BASS's own Media Foundation path.</summary>
    private const int WmaChannelType = 0x10300;

    public static IEnumerable<object[]> Cells() =>
        from encoder in new[] { "ffmpeg", "windows" }
        from rate in new[] { 44100, 48000 }
        from channels in new[] { 1, 2 }
        // Windows' encoder offers no mono at 48 kHz (Fixtures/generate.sh).
        where !(encoder == "windows" && rate == 48000 && channels == 1)
        select new object[] { encoder, rate, channels };

    public static IEnumerable<object[]> WindowsCells() => Cells().Where(c => (string)c[0] == "windows");

    public static IEnumerable<object[]> FfmpegCells() => Cells().Where(c => (string)c[0] == "ffmpeg");

    private static string FixturePath(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        Assert.True(File.Exists(path), $"fixture {name} is missing; run Fixtures/generate.sh");
        return path;
    }

    private static string Wma(string signal, string encoder, int rate, int channels) =>
        FixturePath($"{signal}-{encoder}-{rate}-{channels}.wma");

    private static float[] Sequential(string path)
    {
        using var stream = new MusicBeeBass.Stream(path);
        return stream.ReadAllLeft();
    }

    /// <summary>The generator's source PCM: the lossless FLAC of the same noise.</summary>
    private static float[] Source(int rate, int channels) =>
        TestAudio.LeftChannel(TrackSources.Open(FixturePath($"noise-{rate}-24-{channels}.flac")));

    /// <summary>Spread through the file, and one 100 frames before its audio ends, inside its
    /// last 2048-sample block: the source's end, or the decode's where ffmpeg's is shorter.
    /// Never in Windows' padding past the source, which is the codec's tail, not audio, and
    /// locates nowhere reliably.</summary>
    private static IEnumerable<long> Targets(int rate, long delivered) =>
        Enumerable.Range(0, 24).Select(i => (long)(rate / 20 + i * (rate * 1.85 / 24)))
            .Concat(new[] { Math.Min(delivered, 2L * rate) - 100 });

    /// <summary>Where a seek landed, in frames: <paramref name="seek"/> runs on a fresh stream
    /// before any read, as a voice seeks to its fade-in point, then the first block read is
    /// located in the sequential decode near the target.</summary>
    private static long Landing(string path, float[] sequential, long target, Action<MusicBeeBass.Stream> seek)
    {
        using var stream = new MusicBeeBass.Stream(path);
        seek(stream);
        var block = new float[1024];
        var n = stream.ReadLeft(block, block.Length);
        Assert.True(n > 0, $"nothing could be read after a seek to {target}");
        var (landed, similarity) = TestAudio.Locate(sequential, block.AsSpan(0, n),
            (int)target - stream.Rate / 8, (int)target + stream.Rate / 8);
        Assert.True(similarity > 0.9, $"the block read after a seek to {target} matches nowhere near it ({similarity:F3})");
        return landed;
    }

    [Fact]
    public void TheMeasuredBass_IsMusicBees_2_4_17()
    {
        Assert.Equal(0x02041100, MusicBeeBass.Version);
    }

    [Theory, MemberData(nameof(WindowsCells))]
    public void BassSeek_OnWindowsEncodedWma_LandsLateBy0To2ms_AndPastTheOneMsPromise(string encoder, int rate, int channels)
    {
        var path = Wma("noise", encoder, rate, channels);
        var sequential = Sequential(path);
        var errors = Targets(rate, sequential.Length)
            .Select(t => Landing(path, sequential, t, s => Assert.True(s.Seek(t))) - t)
            .ToList();

        var report = string.Join(" ", errors.Select(e => $"{e * 1000.0 / rate:F2}"));
        Assert.True(errors.All(e => e >= 0 && e <= 2 * rate / 1000), $"landings in ms after their targets: {report}");
        Assert.True(errors.Any(e => e > rate / 1000), $"every landing within 1 ms, so BASS's seek may now be exact: {report}");
    }

    [Theory, MemberData(nameof(FfmpegCells))]
    public void BassSeek_OnFfmpegEncodedWma_LandsAboutOneBlockEarly(string encoder, int rate, int channels)
    {
        var path = Wma("noise", encoder, rate, channels);
        var sequential = Sequential(path);
        var errors = Targets(rate, sequential.Length)
            .Select(t => Landing(path, sequential, t, s => Assert.True(s.Seek(t))) - t)
            .ToList();

        // Measured -2072 to -1980 frames: a 2048-sample WMA block, give or take 1 ms.
        Assert.True(errors.All(e => Math.Abs(e + 2048) <= 100),
            $"landings in frames from their targets: {string.Join(" ", errors)}");
    }

    [Theory, MemberData(nameof(Cells))]
    public void DecodeToFromTheStart_LandsOnTheSample(string encoder, int rate, int channels)
    {
        var path = Wma("noise", encoder, rate, channels);
        var sequential = Sequential(path);
        foreach (var target in Targets(rate, sequential.Length))
        {
            Assert.Equal(target, Landing(path, sequential, target, s => Assert.True(s.DecodeTo(target))));
        }
    }

    [Theory, MemberData(nameof(WindowsCells))]
    public void WindowsEncodedWma_DecodesAsTheWholeSourceFromSample0_ThenPadding(string encoder, int rate, int channels)
    {
        var source = Source(rate, channels);
        using var stream = new MusicBeeBass.Stream(Wma("noise", encoder, rate, channels));
        Assert.Equal(WmaChannelType, stream.ChannelType);
        var reported = stream.LengthFrames;
        var decoded = stream.ReadAllLeft();

        var (first, firstSimilarity) = TestAudio.Locate(decoded, source.AsSpan(0, 4096), 0, 4000);
        var (last, lastSimilarity) = TestAudio.Locate(decoded, source.AsSpan(source.Length - 4096),
            source.Length - 4096 - 4000, source.Length - 4096 + 4000);
        Assert.True(firstSimilarity > 0.9 && first == 0, $"the source starts at {first} ({firstSimilarity:F3}), not 0");
        Assert.True(lastSimilarity > 0.9 && last == source.Length - 4096,
            $"the source's last block is at {last} ({lastSimilarity:F3}), not {source.Length - 4096}");
        // Padded past the source by under one 2048-sample block.
        Assert.InRange(decoded.Length - source.Length, 1, 2048);
        // The length is the padded one, to the millisecond, not to the frame.
        Assert.InRange(decoded.Length - reported, 0, rate / 1000);
    }

    [Theory, MemberData(nameof(FfmpegCells))]
    public void FfmpegEncodedWma_DecodesFromSample0_ButLosesTheLastPartialBlock(string encoder, int rate, int channels)
    {
        var source = Source(rate, channels);
        using var stream = new MusicBeeBass.Stream(Wma("noise", encoder, rate, channels));
        Assert.Equal(WmaChannelType, stream.ChannelType);
        var reported = stream.LengthFrames;
        var decoded = stream.ReadAllLeft();

        var (first, firstSimilarity) = TestAudio.Locate(decoded, source.AsSpan(0, 4096), 0, 4000);
        Assert.True(firstSimilarity > 0.9 && first == 0, $"the source starts at {first} ({firstSimilarity:F3}), not 0");
        Assert.Equal(0, decoded.Length % 2048);
        Assert.InRange(source.Length - decoded.Length, 1, 2047);
        Assert.NotEqual(decoded.Length, reported);
    }

    [Theory, MemberData(nameof(Cells))]
    public void Tone_IsPure(string encoder, int rate, int channels)
    {
        var left = Sequential(Wma("tone", encoder, rate, channels));
        // 100 cycles of 1 kHz, a tenth of a second in.
        var purity = TestAudio.Purity(left, rate / 10, rate / 10, 1000, rate);
        Assert.True(purity >= 0.999, $"purity {purity:F5} < 0.999");
    }
}
