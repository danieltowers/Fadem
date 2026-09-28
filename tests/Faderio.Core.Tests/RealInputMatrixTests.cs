using Faderio.Core.Domain;
using Faderio.Core.Playback;

namespace Faderio.Core.Tests;

/// <summary>The three ways the add-time probe learns an MP3's length (ADR 0010), each a
/// promised row of <c>docs/supported-audio.md</c>. The fixture name carries it.</summary>
public enum Mp3Variant
{
    /// <summary>Constant bitrate with LAME's Info tag: the frame count, less the encoder's delay
    /// and padding, is in the tag.</summary>
    CbrInfo,
    /// <summary>Constant bitrate, no Xing/Info tag: audio bytes over the frame size, whole frames.</summary>
    CbrBare,
    /// <summary>Variable bitrate with a Xing tag: as <see cref="CbrInfo"/>.</summary>
    Vbr,
}

/// <summary>
/// Seam S2 (ticket 06): real files through the production decoder chain, four assertions on
/// every cell of the promised table in <c>docs/supported-audio.md</c> (ticket 08):
/// <list type="number">
/// <item><b>Completeness</b>: a drain renders every frame the decoder reports.</item>
/// <item><b>Purity</b>: a 1 kHz tone comes out as that tone (ticket 01's Goertzel measure).</item>
/// <item><b>Seek landing</b>: a seek lands within 1 ms of where it was asked to (ADR 0009),
/// found by cross-correlating the first block read after the seek against a sequential decode
/// of the same pink-noise file (ticket 02's method).</item>
/// <item><b>Length</b>: the length the add-time probe issues from the header equals the decoded
/// length, exactly, in frames (ADR 0010).</item>
/// </list>
/// v2 fed only 16-bit 44.1 kHz WAV and asserted length nowhere, which is how every file above
/// 44.1 kHz was truncated and every 24-bit WAV rendered as silence while 92 tests passed
/// (ticket 01). WAV cells are generated at test time; FLAC and MP3 cells are checked-in
/// fixtures from <c>Fixtures/generate.sh</c>, so a missing fixture is a red cell, never a skip.
/// A row in the doc names the theories here that prove it; a theory here proves nothing the
/// product claims unless its row exists.
/// </summary>
public class RealInputMatrixTests
{
    private const double Seconds = 2.0;
    private const int EngineRate = 44100; // the fake output declares it (ADR 0008)
    private const long ExpectedFrames = (long)(Seconds * EngineRate); // after normalisation
    private const int Tone = 1000;

    /// <summary>The authoring promise (ticket 09, ADR 0009): 1 ms at every promised cell.</summary>
    private const double LandingToleranceSeconds = 0.001;

    /// <summary>Frames a seek may land from its target: 1 ms at the engine rate, ADR 0009.</summary>
    private const long LandingToleranceFrames = (long)(LandingToleranceSeconds * EngineRate);

    /// <summary>Frames read after a seek and correlated back. Pink noise this long matches one
    /// offset with similarity near 1 and every other below about 0.2.</summary>
    private const int LandingWindow = 1024;

    // ── The cells ────────────────────────────────────────────────────────────────────────

    public static IEnumerable<object[]> WavCells() =>
        from rate in new[] { 44100, 48000, 96000 }
        from bits in new[] { 16, 24 }
        from channels in new[] { 1, 2 }
        select new object[] { rate, bits, channels };

    public static IEnumerable<object[]> FlacCells() => WavCells();

    public static IEnumerable<object[]> Mp3Cells() =>
        from variant in new[] { Mp3Variant.CbrInfo, Mp3Variant.CbrBare, Mp3Variant.Vbr }
        from rate in new[] { 44100, 48000 }
        from channels in new[] { 1, 2 }
        select new object[] { variant, rate, channels };

    /// <summary>The variants whose LAME tag the probe trims by (issue 18).</summary>
    public static IEnumerable<object[]> TaggedMp3Cells() =>
        from cell in Mp3Cells()
        where (Mp3Variant)cell[0] != Mp3Variant.CbrBare
        select cell;

    private static string FixturePath(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        Assert.True(File.Exists(path), $"fixture {name} is missing; run Fixtures/generate.sh");
        return path;
    }

    private static string FlacFixture(string signal, int rate, int bits, int channels) =>
        FixturePath($"{signal}-{rate}-{bits}-{channels}.flac");

    private static string Mp3Fixture(string signal, Mp3Variant variant, int rate, int channels) =>
        FixturePath($"{signal}-{variant.ToString().ToLowerInvariant()}-{rate}-{channels}.mp3");

    private static string ToneWav(int rate, int bits, int channels)
    {
        var w = 2.0 * Math.PI * Tone / rate;
        return TestAudio.WriteWavAt("tone", Seconds, f => 0.4 * Math.Sin(w * f), rate, bits, channels);
    }

    private static string NoiseWav(int rate, int bits, int channels)
    {
        var noise = TestAudio.PinkNoise((long)(Seconds * rate));
        return TestAudio.WriteWavAt("noise", Seconds, f => noise[f], rate, bits, channels);
    }

    // ── The chain and the measures ───────────────────────────────────────────────────────

    /// <summary>Opens through the production factory and normaliser — the exact path a voice
    /// takes into the mixer.</summary>
    private static ITrackSource OpenNormalised(string path) =>
        NormalizedTrackSource.Wrap(TrackSources.Open(path), EngineRate);

    /// <summary>Reads to exhaustion in mixer-sized pulls; returns frames read and the left
    /// channel, so completeness and purity come from one pass.</summary>
    private static (long Frames, List<float> Left) Drain(ITrackSource source)
    {
        var buffer = new float[4410 * 2];
        var left = new List<float>();
        long frames = 0;
        int n;
        while ((n = source.Read(buffer, 0, 4410)) > 0)
        {
            for (var f = 0; f < n; f++)
            {
                left.Add(buffer[f * 2]);
            }
            frames += n;
        }
        return (frames, left);
    }

    private static void AssertComplete(string path)
    {
        using var source = OpenNormalised(path);
        var (frames, _) = Drain(source);
        // The fixture is 2 s; a lossy encoder pads it to whole frames (LAME: up to two of 1152).
        Assert.InRange(source.TotalFrames, ExpectedFrames, ExpectedFrames + 2 * 1152);
        // Exactly, resampled or not: the normaliser rounds the length once and the resampler
        // delivers that many (measured in ticket 14; v2 needed a 100-frame allowance here).
        Assert.True(frames == source.TotalFrames,
            $"rendered {frames} of the {source.TotalFrames} frames the decoder reports");
        Assert.Equal(source.TotalFrames, source.Position); // the getter agrees with the length it reports
    }

    private static void AssertPure(string path)
    {
        using var source = OpenNormalised(path);
        var (_, left) = Drain(source);
        // Skip the resampler's warm-up and any encoder delay; 4410 frames at 44.1 kHz is
        // exactly 100 cycles of 1 kHz, which Goertzel needs.
        var purity = TestAudio.Purity(left, start: 4410, frames: 4410, Tone, EngineRate);
        Assert.True(purity >= 0.999, $"purity {purity:F5} < 0.999");
    }

    /// <summary>The fourth assertion (ADR 0010): the header's length, as the add-time probe reads
    /// it, is the decoder's length, exactly, in the file's own frames.</summary>
    private static void AssertHeaderLengthEqualsDecoded(string path)
    {
        var rejection = AudioProbe.Probe(path, out var probed);
        Assert.True(rejection is null, $"probe rejected the cell: {rejection}");
        using var decoded = TrackSources.Open(path);
        Assert.Equal(decoded.TotalFrames, probed!.Frames);
    }

    /// <summary>The 2 s the generator encoded, in the file's own frames.</summary>
    private static long SourceFrames(int rate) => (long)(Seconds * rate);

    /// <summary>What LAME pads 2 s out to in whole 1152-sample frames: 78 at 44.1 kHz, 85 at 48.</summary>
    private static long WholeFrames(int rate) => rate == 44100 ? 78 * 1152 : 85 * 1152;

    /// <summary>The decoder, in the file's own frames, reports and delivers exactly
    /// <paramref name="expected"/>.</summary>
    private static void AssertDecodedFrames(string path, long expected)
    {
        using var decoded = TrackSources.Open(path);
        Assert.Equal(expected, decoded.TotalFrames);
        Assert.Equal(expected, TestAudio.LeftChannel(TrackSources.Open(path)).Length);
    }

    /// <summary>The decode, in the file's own frames, is the generator's source PCM sample for
    /// sample: its first block sits at offset 0 of the source and its last at the source's end.
    /// The source is the lossless FLAC fixture of the same pink noise (Fixtures/generate.sh).</summary>
    private static void AssertIsTheSource(string mp3, int rate, int channels)
    {
        var source = TestAudio.LeftChannel(TrackSources.Open(FlacFixture("noise", rate, 24, channels)));
        var decoded = TestAudio.LeftChannel(TrackSources.Open(mp3));
        const int block = 4096;

        var (first, firstSimilarity) = TestAudio.Locate(source, decoded.AsSpan(0, block), 0, 2000);
        var (last, lastSimilarity) = TestAudio.Locate(source, decoded.AsSpan(decoded.Length - block),
            source.Length - block - 2000, source.Length - block);

        Assert.True(firstSimilarity > 0.95 && first == 0,
            $"the decode's first block is the source's at {first} (similarity {firstSimilarity:F3}), not 0");
        Assert.True(lastSimilarity > 0.95 && last == source.Length - block,
            $"the decode's last block is the source's at {last} (similarity {lastSimilarity:F3}), " +
            $"not {source.Length - block}");
    }

    /// <summary>Where a seek to <paramref name="target"/> landed, in engine frames, by ticket
    /// 02's method: a fresh source is seeked before any read (the production order: a voice
    /// seeks to its fade-in point, then plays), the first block it yields is located in a
    /// sequential decode of the same file by cross-correlation, and that offset is the landing.
    /// No assumption about the decoder's gain, delay or block size survives into the answer.</summary>
    private static long Landing(string path, long target)
    {
        List<float> reference;
        using (var sequential = OpenNormalised(path))
        {
            (_, reference) = Drain(sequential);
        }

        using var source = OpenNormalised(path);
        source.Position = target;
        var block = new float[LandingWindow * 2];
        var got = 0;
        int n;
        while (got < LandingWindow && (n = source.Read(block, got * 2, LandingWindow - got)) > 0)
        {
            got += n;
        }
        Assert.True(got > 0, "nothing could be read after the seek");
        var left = new float[got];
        for (var f = 0; f < got; f++)
        {
            left[f] = block[f * 2];
        }

        // Near the target first (the suite runs unoptimised, and the whole search is 4× the
        // cost of the cell); anywhere at all if that misses, so a red cell says where it went.
        var (offset, similarity) = TestAudio.Locate(reference, left, (int)target - EngineRate / 4, (int)target + EngineRate / 4);
        if (similarity < 0.9)
        {
            (offset, similarity) = TestAudio.Locate(reference, left);
        }
        LastLandingReport = $"target={target} landed={offset} similarity={similarity:F3} " +
                            $"blockFrames={got} sequentialFrames={reference.Count}";
        Assert.True(similarity > 0.9, $"the block read after the seek matches nowhere in a sequential decode; {LastLandingReport}");
        return offset;
    }

    /// <summary>Set by <see cref="Landing"/>; put it in the assertion message so a red cell
    /// says what the decoder actually did.</summary>
    [ThreadStatic] private static string? LastLandingReport;

    private static void AssertLanded(long landed, long target)
    {
        Assert.True(Math.Abs(landed - target) <= LandingToleranceFrames,
            $"off by {(landed - target) * 1000.0 / EngineRate:F1} ms; {LastLandingReport}");
    }

    /// <summary>A fade-in point three quarters of the way in.</summary>
    private static long SeekTarget => ExpectedFrames * 3 / 4;

    /// <summary>A point inside the decoder's last block: FLAC frames are 4096 or 4608 samples
    /// and MP3 frames 1152, and 2 s is not a multiple of either at any promised rate, so 100
    /// frames from the end is inside the last block on every cell.</summary>
    private static long LastBlockTarget => ExpectedFrames - 100;

    // ── WAV cells, generated at test time ────────────────────────────────────────────────

    [Theory, MemberData(nameof(WavCells))]
    public void Wav_ReadsToCompletion(int rate, int bits, int channels)
    {
        // Defect A: NormalizedTrackSource.Position inverted its conversion, so Position raced
        // ahead of TotalFrames by (rate/44100)² and the voice ended early. Defect B: no
        // Extensible case, so every 24-bit block threw and the track was silent.
        AssertComplete(TestAudio.WriteWavAt("complete", Seconds, _ => 0.4, rate, bits, channels));
    }

    [Theory, MemberData(nameof(WavCells))]
    public void Wav_ToneIsPure(int rate, int bits, int channels) =>
        AssertPure(ToneWav(rate, bits, channels));

    [Theory, MemberData(nameof(WavCells))]
    public void Wav_HeaderLengthEqualsDecoded(int rate, int bits, int channels) =>
        AssertHeaderLengthEqualsDecoded(ToneWav(rate, bits, channels));

    [Theory, MemberData(nameof(WavCells))]
    public void Wav_SeekBeforeFirstRead_LandsWithin1ms(int rate, int bits, int channels) =>
        AssertLanded(Landing(NoiseWav(rate, bits, channels), SeekTarget), SeekTarget);

    [Theory, MemberData(nameof(WavCells))]
    public void Wav_SeekHalfway_ReadsHalfTheFrames(int rate, int bits, int channels)
    {
        // The setter half of defect A: seeking to the normalised midpoint landed at the wrong
        // inner frame, so the remainder was the wrong length.
        var path = TestAudio.WriteWavAt("seek", Seconds, _ => 0.4, rate, bits, channels);
        using var source = OpenNormalised(path);

        source.Position = source.TotalFrames / 2;
        var (frames, _) = Drain(source);

        Assert.InRange(frames, ExpectedFrames / 2 - 1, ExpectedFrames / 2 + 1); // the midpoint rounds once on the way in
    }

    [Theory]
    [InlineData(44100)]
    [InlineData(48000)]
    [InlineData(96000)]
    public void Engine_PlaysWholeTrack_ThenFallsSilent(int rate)
    {
        // The completeness assertion ticket 06 asked for, end to end: audible output lasts the
        // whole track, not (rate/44100)² of it. v2's suite asserted rendered length nowhere.
        var path = TestAudio.WriteWavAt("full", Seconds, _ => 0.4, rate, 16, 2);
        using var harness = TestEngine.Create();
        harness.Add(path);
        harness.Engine.Play();
        harness.Output.Advance((int)(5 * EngineRate));

        var rendered = harness.Rendered;
        var lastAudible = rendered.Frames - 1;
        while (lastAudible >= 0 && Math.Abs(rendered.Sample(lastAudible, 0)) < 0.2)
        {
            lastAudible--;
        }

        Assert.InRange(lastAudible, ExpectedFrames - 4410, ExpectedFrames + 4410); // within one block
    }

    // ── FLAC cells, checked-in fixtures ──────────────────────────────────────────────────

    [Theory, MemberData(nameof(FlacCells))]
    public void Flac_ReadsToCompletion(int rate, int bits, int channels) =>
        AssertComplete(FlacFixture("tone", rate, bits, channels));

    [Theory, MemberData(nameof(FlacCells))]
    public void Flac_ToneIsPure(int rate, int bits, int channels) =>
        AssertPure(FlacFixture("tone", rate, bits, channels));

    [Theory, MemberData(nameof(FlacCells))]
    public void Flac_HeaderLengthEqualsDecoded(int rate, int bits, int channels) =>
        AssertHeaderLengthEqualsDecoded(FlacFixture("tone", rate, bits, channels));

    [Theory, MemberData(nameof(FlacCells))]
    public void Flac_SeekBeforeFirstRead_LandsWithin1ms(int rate, int bits, int channels)
    {
        // Ticket 02: the Media Foundation FLAC reader silently dropped a seek issued before any
        // read (an authored fade-in point discarded entirely, -2267 ms), and with a warm-up read
        // still landed on the next decoder block boundary, +67 ms (ticket 07). Ticket 09 set
        // the promise at 1 ms and ticket 15 swapped the decoder for one that lands on the frame
        // before the target and discards to the exact sample.
        AssertLanded(Landing(FlacFixture("noise", rate, bits, channels), SeekTarget), SeekTarget);
    }

    [Theory, MemberData(nameof(FlacCells))]
    public void Flac_SeekIntoLastBlock_LandsWithin1ms(int rate, int bits, int channels)
    {
        // BunLabs.NAudio.Flac 2.0.1 seeks by finding the first frame that starts at or after
        // the target, so a target inside the last frame matched nothing and the seek was
        // silently a no-op: the voice would have played from wherever it was. FlacTrackReader
        // seeks into the last frame by hand (ticket 15).
        AssertLanded(Landing(FlacFixture("noise", rate, bits, channels), LastBlockTarget), LastBlockTarget);
    }

    // ── MP3 cells, checked-in fixtures ───────────────────────────────────────────────────

    [Theory, MemberData(nameof(Mp3Cells))]
    public void Mp3_ReadsToCompletion(Mp3Variant variant, int rate, int channels) =>
        AssertComplete(Mp3Fixture("tone", variant, rate, channels));

    [Theory, MemberData(nameof(Mp3Cells))]
    public void Mp3_ToneIsPure(Mp3Variant variant, int rate, int channels) =>
        AssertPure(Mp3Fixture("tone", variant, rate, channels));

    [Theory, MemberData(nameof(Mp3Cells))]
    public void Mp3_HeaderLengthEqualsDecoded(Mp3Variant variant, int rate, int channels)
    {
        // This is where the three variants earn their rows (ticket 10): the Xing/Info frame
        // count less the LAME tag's delay and padding for CbrInfo and Vbr, which is the source's
        // own sample count (issue 18); the audio bytes over the frame size for CbrBare, whole
        // frames, since a bare file says nothing about its padding.
        var path = Mp3Fixture("tone", variant, rate, channels);
        AssertHeaderLengthEqualsDecoded(path);
        AssertDecodedFrames(path, variant == Mp3Variant.CbrBare ? WholeFrames(rate) : SourceFrames(rate));
    }

    [Theory, MemberData(nameof(TaggedMp3Cells))]
    public void Mp3_TaggedDecode_IsTheSourceFromItsFirstSample(Mp3Variant variant, int rate, int channels)
    {
        // 0:00 is the first encoded sample: the trimming reader skips the encoder's delay and
        // the ACM decoder's own (Mp3DecoderOffsetTests), and ends where the source ended.
        AssertIsTheSource(Mp3Fixture("noise", variant, rate, channels), rate, channels);
    }

    [Theory, MemberData(nameof(TaggedMp3Cells))]
    public void Mp3_LameEncoderString_IsTrimmedLikeFfmpegsLavc(Mp3Variant variant, int rate, int channels)
    {
        // Library files say LAME3.9x where ffmpeg's fixtures say Lavc62.23. lame.exe is not
        // available to the generator, so the tag is crafted (CraftedMp3); there is no encoder
        // allowlist, so it must trim the same.
        var tone = CraftedMp3.WithEncoder(Mp3Fixture("tone", variant, rate, channels), "LAME3.99r");
        var noise = CraftedMp3.WithEncoder(Mp3Fixture("noise", variant, rate, channels), "LAME3.99r");

        AssertHeaderLengthEqualsDecoded(tone);
        AssertDecodedFrames(tone, SourceFrames(rate));
        AssertIsTheSource(noise, rate, channels);
    }

    [Fact]
    public void Mp3_TagPaddingShorterThanTheDecodersDelay_EndsInSilenceAtItsLength()
    {
        // 61 of the library's tagged MP3s (60 by an old ffmpeg, Lavc57) record padding 0. The decoder's delay (D,
        // 529) then carries the source's last samples past the last frame, where no decoder
        // reaches, so the trimming reader ends in that much silence and the length stays the
        // one the probe issued: 78 x 1152 - 576 - 0, of which the last 529 are silence.
        var path = CraftedMp3.WithDelayAndPadding(Mp3Fixture("tone", Mp3Variant.CbrInfo, 44100, 2), 576, 0);
        const long length = 78 * 1152 - 576;

        AssertHeaderLengthEqualsDecoded(path);
        AssertDecodedFrames(path, length);
        using var decoded = TrackSources.Open(path);
        decoded.Position = length - 2000; // inside the tone, which ends 1080 frames before the end
        var tail = new float[2000 * 2];
        Assert.Equal(2000, decoded.Read(tail, 0, 2000));
        Assert.Contains(tail.AsSpan()[..(900 * 2)].ToArray(), s => Math.Abs(s) > 0.1);   // the tone
        Assert.Contains(tail.AsSpan()[(1000 * 2)..(1471 * 2)].ToArray(), s => s != 0f);  // the encoder's padding, decoded
        Assert.All(tail.AsSpan()[(1471 * 2)..].ToArray(), s => Assert.Equal(0f, s));     // then D of silence
        Assert.Equal(length, decoded.Position);
    }

    [Fact]
    public void Mp3_CutAfterEncoding_WithinTheMusicLengthAllowance_EndsWhereItsAudioEnds()
    {
        // A cut of under 10 KiB still passes the MusicLength check, so the file is trimmed by its
        // tag and the probe issues the source count; the decoder then stops short. The reader
        // does not pad that with silence: the track ends where the audio ends, which is the
        // engine's one notice (ADR 0010).
        var fixture = Mp3Fixture("tone", Mp3Variant.CbrInfo, 44100, 2);
        var bytes = File.ReadAllBytes(fixture);
        var path = Path.Combine(TestAudio.Dir, $"cut-{Guid.NewGuid():N}.mp3");
        Directory.CreateDirectory(TestAudio.Dir);
        File.WriteAllBytes(path, bytes.AsSpan()[..^(8 * 1024)].ToArray());

        Assert.Null(AudioProbe.Probe(path, out var probed));
        Assert.Equal(88200, probed!.Frames);
        var frames = TestAudio.LeftChannel(TrackSources.Open(path)).Length;
        Assert.InRange(frames, 88200 - 8 * 1024 / 1044 * 1152 - 2 * 1152, 88200 - 1152);
    }

    [Theory, MemberData(nameof(TaggedMp3Cells))]
    public void Mp3_TagFailingValidation_KeepsWholeFrames(Mp3Variant variant, int rate, int channels)
    {
        // A tag whose CRC fails is not trusted: whole frames, as for a bare file, and silently.
        var path = CraftedMp3.WithBrokenTagCrc(Mp3Fixture("tone", variant, rate, channels));

        AssertHeaderLengthEqualsDecoded(path);
        AssertDecodedFrames(path, WholeFrames(rate));
        AssertPure(path);
    }

    [Theory, MemberData(nameof(Mp3Cells))]
    public void Mp3_SeekBeforeFirstRead_LandsWithin1ms(Mp3Variant variant, int rate, int channels) =>
        AssertLanded(Landing(Mp3Fixture("noise", variant, rate, channels), SeekTarget), SeekTarget);

    [Theory, MemberData(nameof(Mp3Cells))]
    public void Mp3_SeekIntoLastBlock_LandsWithin1ms(Mp3Variant variant, int rate, int channels) =>
        AssertLanded(Landing(Mp3Fixture("noise", variant, rate, channels), LastBlockTarget), LastBlockTarget);
}
