using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Settings;

namespace Faderio.Core.Tests;

/// <summary>
/// Build issue 13: the ReplayGain toggle applies at once to every live voice, each moving to its
/// own tag's gain. The change is taken at the decode position and heard one device buffer later
/// (the audible-time rule); each voice's base gain ramps over a few milliseconds, so there is no
/// click.
/// </summary>
public sealed class ReplayGainSwitchTests
{
    private const int Rate = 44100;

    private static int At(double seconds) => (int)(seconds * Rate);

    private static readonly PlayerSettings Off = new() { ReplayGainMode = ReplayGainMode.Off };
    private static readonly PlayerSettings On = new();

    /// <summary>The largest step between neighbouring samples of the left channel over the
    /// range: a click is one big step.</summary>
    private static double LargestStep(RenderedAudio rendered, long from, long to)
    {
        var largest = 0.0;
        for (var f = from + 1; f < to; f++)
        {
            largest = Math.Max(largest, Math.Abs(rendered.Sample(f, 0) - rendered.Sample(f - 1, 0)));
        }
        return largest;
    }

    [Fact]
    public void TogglingOff_MidTrack_TakesThePlayingTrackToUnityGain_AtOnce()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        tc.Engine.AddTracks(new List<Track> { tc.TrackOf(a, replayGainDb: -6.0) });
        tc.Engine.Play();
        tc.Output.Advance(At(1));
        Assert.Equal(0.4 * ReplayGain.DbToMultiplier(-6), tc.Rendered.DcLevel(At(1)), 3);

        tc.Engine.ChangeSettings(Off);
        tc.Output.Advance(At(0.05));

        // Within 20 ms of the toggle, the track is at its untagged level.
        Assert.Equal(0.4, tc.Rendered.DcLevel(At(1.02)), 3);
    }

    [Fact]
    public void TogglingBackOn_ReturnsThePlayingTrackToItsTagsGain()
    {
        using var tc = TestEngine.Create(Off);
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        tc.Engine.AddTracks(new List<Track> { tc.TrackOf(a, replayGainDb: -6.0) });
        tc.Engine.Play();
        tc.Output.Advance(At(1));
        Assert.Equal(0.4, tc.Rendered.DcLevel(At(1)), 3);

        tc.Engine.ChangeSettings(On);
        tc.Output.Advance(At(0.05));

        Assert.Equal(0.4 * ReplayGain.DbToMultiplier(-6), tc.Rendered.DcLevel(At(1.02)), 3);
    }

    [Fact]
    public void TheToggle_RampsTheGain_WithNoStepInTheRenderedSamples()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        tc.Engine.AddTracks(new List<Track> { tc.TrackOf(a, replayGainDb: -6.0) });
        tc.Engine.Play();
        tc.Output.Advance(At(1));

        tc.Engine.ChangeSettings(Off);
        tc.Output.Advance(At(0.05));

        // A 0.2 jump between two samples is a click; the ramp spreads it over a few milliseconds.
        Assert.True(LargestStep(tc.Rendered, At(1) - 10, At(1.05)) < 0.005);
        // It is a ramp, not an instant change: the first sample after the toggle is still near
        // the old level.
        Assert.True(tc.Rendered.Sample(At(1), 0) < 0.25);
    }

    [Fact]
    public void WithADeviceBuffer_TheToggleStartsAfterWhatTheDeviceHolds_OneBufferAfterThePlayhead()
    {
        const int buffer = Rate / 10; // 100 ms
        using var tc = TestEngine.Create(latency: buffer);
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        tc.Engine.AddTracks(new List<Track> { tc.TrackOf(a, replayGainDb: -6.0) });
        tc.Engine.Play();
        tc.Output.Advance(At(1));
        Assert.Equal(0.9, tc.Playhead.TotalSeconds, 3); // the listener is a buffer behind

        tc.Engine.ChangeSettings(Off);
        tc.Output.Advance(At(0.05));

        // What the device already held is untouched; the change begins in the next frame
        // rendered, which the listener hears when the playhead reaches 1.0 s.
        Assert.Equal(0.4 * ReplayGain.DbToMultiplier(-6), tc.Rendered.Sample(At(1) - 1, 0), 3);
        Assert.Equal(0.4, tc.Rendered.DcLevel(At(1.02)), 3);
    }

    [Fact]
    public void MidCrossfade_BothVoicesRamp_WithNoStepInTheRenderedSamples()
    {
        using var tc = TestEngine.Create(Off with
        {
            UnauthoredAdjacency = UnauthoredAdjacency.DefaultCrossfade,
            DefaultCrossfadeSeconds = 2,
        });
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.2);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        tc.Engine.AddTracks(new List<Track>
        {
            tc.TrackOf(a, replayGainDb: -6.0),
            tc.TrackOf(b, replayGainDb: +6.0),
        });
        tc.Engine.Play();
        tc.Output.Advance(At(2.5));

        tc.Engine.ChangeSettings(On);
        tc.Output.Advance(At(0.1));

        Assert.True(LargestStep(tc.Rendered, At(2.5) - 10, At(2.6)) < 0.005);
    }

    [Fact]
    public void AnUntaggedTrack_IsUnaffectedByTheToggle()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        tc.Engine.AddTracks(new List<Track> { tc.TrackOf(a) });
        tc.Engine.Play();
        tc.Output.Advance(At(1));

        tc.Engine.ChangeSettings(Off);
        tc.Output.Advance(At(0.5));
        tc.Engine.ChangeSettings(On);
        tc.Output.Advance(At(0.5));

        for (var f = At(0.5); f < At(2); f++)
        {
            Assert.Equal(0.4, tc.Rendered.Sample(f, 0), 4);
        }
    }

    [Fact]
    public void MidCrossfade_EachVoiceMovesToItsOwnTagsGain()
    {
        using var tc = TestEngine.Create(Off with
        {
            UnauthoredAdjacency = UnauthoredAdjacency.DefaultCrossfade,
            DefaultCrossfadeSeconds = 2,
        });
        var a = TestAudio.WriteToneWav("a.wav", 4, 1000);
        var b = TestAudio.WriteToneWav("b.wav", 4, 2000);
        tc.Engine.AddTracks(new List<Track>
        {
            tc.TrackOf(a, replayGainDb: -6.0),
            tc.TrackOf(b, replayGainDb: +6.0),
        });
        tc.Engine.Play();
        tc.Output.Advance(At(2.5)); // a → b fades from 2.0 s to 4.0 s

        tc.Engine.ChangeSettings(On);
        tc.Output.Advance(At(0.5));

        // Half way (3.0 s), each at equal power (cos π/4 = sin π/4) times its own tag's gain.
        var half = Math.Sqrt(0.5);
        Assert.Equal(0.4 * half * ReplayGain.DbToMultiplier(-6), tc.Rendered.ToneAmplitude(1000, At(3)), 2);
        Assert.Equal(0.4 * half * ReplayGain.DbToMultiplier(+6), tc.Rendered.ToneAmplitude(2000, At(3)), 2);
    }

    [Fact]
    public void TheNextTrack_StartsAtTheGainSetWhileTheOneBeforeItPlayed()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 2, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 2, 0.4);
        tc.Engine.AddTracks(new List<Track> { tc.TrackOf(a, replayGainDb: -6.0), tc.TrackOf(b, replayGainDb: -6.0) });
        tc.Engine.Play();
        tc.Output.Advance(At(1));

        tc.Engine.ChangeSettings(Off);
        tc.Output.Advance(At(2));

        Assert.Equal(0.4, tc.Rendered.DcLevel(At(2.5)), 3);
    }
}
