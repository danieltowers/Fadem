using Faderio.Core.Domain;
using Faderio.Core.Playback;

namespace Faderio.Core.Tests;

/// <summary>
/// Seam S2 (ticket 06): real files through the production decoder chain, three assertions per
/// cell — completeness, purity, seek. v2 fed only 16-bit 44.1 kHz WAV and asserted length
/// nowhere, which is how every file above 44.1 kHz was truncated and every 24-bit WAV rendered
/// as silence while 92 tests passed (ticket 01). Each theory here is a regression test for one
/// of those. This is the skeleton; ticket 14 fills the compressed cells.
/// </summary>
public class RealInputMatrixTests
{
    private const double Seconds = 2.0;
    private const int EngineRate = NormalizedTrackSource.EngineSampleRate;
    private const long ExpectedFrames = (long)(Seconds * EngineRate); // after normalisation
    private const int Tone = 1000;

    public static IEnumerable<object[]> WavCells() =>
        from rate in new[] { 44100, 48000, 96000 }
        from bits in new[] { 16, 24 }
        from channels in new[] { 1, 2 }
        select new object[] { rate, bits, channels };

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    /// <summary>Opens through the production factory and normaliser — the exact path a voice
    /// takes into the mixer.</summary>
    private static ITrackSource OpenNormalised(string path) =>
        NormalizedTrackSource.Wrap(new NAudioAudioSourceFactory().Open(new Track { FilePath = path }));

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

    // ── WAV cells, generated at test time ────────────────────────────────────────────────

    [Theory, MemberData(nameof(WavCells))]
    public void Wav_ReadsToCompletion(int rate, int bits, int channels)
    {
        // Defect A: NormalizedTrackSource.Position inverted its conversion, so Position raced
        // ahead of TotalFrames by (rate/44100)² and the voice ended early. Defect B: no
        // Extensible case, so every 24-bit block threw and the track was silent.
        var path = TestAudio.WriteWavAt("complete", Seconds, _ => 0.4, rate, bits, channels);
        using var source = OpenNormalised(path);

        var (frames, _) = Drain(source);

        Assert.InRange(frames, ExpectedFrames - 100, ExpectedFrames + 100);
        Assert.Equal(source.TotalFrames, source.Position); // the getter agrees with the length it reports
    }

    [Theory, MemberData(nameof(WavCells))]
    public void Wav_ToneIsPure(int rate, int bits, int channels)
    {
        var w = 2.0 * Math.PI * Tone / rate;
        var path = TestAudio.WriteWavAt("tone", Seconds, f => 0.4 * Math.Sin(w * f), rate, bits, channels);
        using var source = OpenNormalised(path);

        var (_, left) = Drain(source);

        // Skip the resampler's warm-up; 4410 frames at 44.1 kHz is exactly 100 cycles of 1 kHz.
        var purity = TestAudio.Purity(left, start: 4410, frames: 4410, Tone, EngineRate);
        Assert.True(purity >= 0.999, $"purity {purity:F5} < 0.999");
    }

    [Theory, MemberData(nameof(WavCells))]
    public void Wav_SeekHalfway_ReadsHalfTheFrames(int rate, int bits, int channels)
    {
        // The setter half of defect A: seeking to the normalised midpoint landed at the wrong
        // inner frame, so the remainder was the wrong length.
        var path = TestAudio.WriteWavAt("seek", Seconds, _ => 0.4, rate, bits, channels);
        using var source = OpenNormalised(path);

        source.Position = source.TotalFrames / 2;
        var (frames, _) = Drain(source);

        Assert.InRange(frames, ExpectedFrames / 2 - 100, ExpectedFrames / 2 + 100);
    }

    [Fact]
    public void Wav_SeekLandsExactly_At44100()
    {
        // A ramp encodes position in amplitude: the first sample after a seek says where it
        // landed. WAV at the engine rate is exact (ticket 02); 1 ms is the tripwire.
        var total = (long)(Seconds * 44100);
        var path = TestAudio.WriteWavAt("ramp", Seconds, f => 0.9 * f / total, 44100, 16, 2);
        using var source = OpenNormalised(path);
        var target = total * 3 / 4;

        source.Position = target;
        var buffer = new float[2];
        Assert.Equal(1, source.Read(buffer, 0, 1));

        var landed = buffer[0] / 0.9 * total;
        Assert.InRange(landed, target - 44, target + 44);
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
        harness.SetQueueOf(path);
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

    /// <summary>Where a seek landed, in frames, by ticket 02's method: ground truth is a full
    /// sequential decode of the same file, and the ramp fixture makes amplitude a linear
    /// function of position, so the sample read after the seek maps back to a frame without
    /// assuming the encoder's gain or the fixture's peak.</summary>
    private static double Landing(string path, long target, bool readFirst)
    {
        double atTarget;
        List<float> left;
        long frames;
        using (var reference = OpenNormalised(path))
        {
            (frames, left) = Drain(reference);
            atTarget = left[(int)target];
        }

        using var source = OpenNormalised(path);
        var buffer = new float[4410 * 2];
        if (readFirst)
        {
            source.Read(buffer, 0, 4410);
        }
        source.Position = target;
        Assert.Equal(1, source.Read(buffer, 0, 1));
        var landed = target * (buffer[0] / atTarget);

        // Enough to diagnose a decoder whose timeline is shifted, without a debugger.
        var probe = string.Join(", ", new[] { 0, 4410, 22050, 44100, 66150, 88000 }
            .Where(i => i < left.Count).Select(i => $"[{i}]={left[i]:F4}"));
        LastLandingReport = $"target={target} landed={landed:F0} seekValue={buffer[0]:F4} " +
                            $"sequentialAtTarget={atTarget:F4} frames={frames} sequential: {probe}";
        return landed;
    }

    /// <summary>Set by <see cref="Landing"/>; put it in the assertion message so a red cell
    /// says what the decoder actually did.</summary>
    private static string LastLandingReport = "";

    private static void AssertLanded(double landed, long target, double toleranceSeconds)
    {
        var tolerance = toleranceSeconds * EngineRate;
        Assert.True(landed >= target - tolerance && landed <= target + tolerance,
            $"off by {(landed - target) / EngineRate * 1000:F0} ms; {LastLandingReport}");
    }

    /// <summary>Pinned characterisation values (ticket 06): the measured residual plus margin,
    /// named for the ticket that will tighten them. FLAC measured +67 ms late on this fixture
    /// (ticket 07); ticket 02 saw 31 ms on its own. OGG measured within 40 ms.</summary>
    private const double FlacPinSeconds = 0.100;
    private const double OggPinSeconds = 0.040;

    // ── Compressed cells, from checked-in fixtures (see Fixtures/generate.sh) ────────────

    [Fact]
    public void Flac_ReadsToCompletion()
    {
        using var source = OpenNormalised(FixturePath("tone-1k.flac"));
        var (frames, _) = Drain(source);
        Assert.InRange(frames, ExpectedFrames - 100, ExpectedFrames + 100);
    }

    [Fact]
    public void Flac_ToneIsPure()
    {
        using var source = OpenNormalised(FixturePath("tone-1k.flac"));
        var (_, left) = Drain(source);
        var purity = TestAudio.Purity(left, start: 4410, frames: 4410, Tone, EngineRate);
        Assert.True(purity >= 0.999, $"purity {purity:F5} < 0.999");
    }

    [Fact]
    public void Flac_SeekBeforeFirstRead_LandsWithin100ms_PendingTicket09()
    {
        // Ticket 02: the Media Foundation FLAC reader silently drops a seek issued before any
        // read, so an authored fade-in point was discarded entirely (-2267 ms). A warm-up read
        // makes the seek stick. This is a PINNED characterisation test: green and documented,
        // catching a regression to the catastrophic case. Ticket 09 sets the real number.
        // Measured while pinning (ticket 07): +67 ms late on this fixture, independent of
        // warm-up size. The catastrophic case would land at 0, 1.5 s off.
        var target = ExpectedFrames * 3 / 4;
        var landed = Landing(FixturePath("ramp.flac"), target, readFirst: false); // production order
        AssertLanded(landed, target, FlacPinSeconds);
    }

    [Fact]
    public void Ogg_ReadsToCompletion()
    {
        using var source = OpenNormalised(FixturePath("ramp.ogg"));
        var (frames, _) = Drain(source);
        Assert.InRange(frames, ExpectedFrames - 100, ExpectedFrames + 100);
    }

    [Fact]
    public void Ogg_SeekAfterFirstRead_Lands()
    {
        // The order the engine happens to survive by (ticket 01, defect C): length, read, seek.
        var target = ExpectedFrames * 3 / 4;
        var landed = Landing(FixturePath("ramp.ogg"), target, readFirst: true);
        AssertLanded(landed, target, OggPinSeconds);
    }

    [Fact]
    public void Ogg_SeekBeforeFirstRead_DoesNotThrow_AndLands()
    {
        // Defect C (ticket 01): VorbisTrackSource.Position was reported to throw out of NVorbis
        // if set before any Read or TotalFrames access. Measured in ticket 07: seek order makes
        // no difference; what NVorbis rejects is ffmpeg's default 1 s page duration (see
        // Fixtures/generate.sh). The constructor now touches the length anyway.
        var target = ExpectedFrames * 3 / 4;
        var landed = Landing(FixturePath("ramp.ogg"), target, readFirst: false);
        AssertLanded(landed, target, OggPinSeconds);
    }
}
