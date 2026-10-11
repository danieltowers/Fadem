using Fadem.Core.Domain;
using Fadem.Core.Playback;

namespace Fadem.Core.Tests;

/// <summary>The clip light, through the seam (build issue 17): a mixed output sample above full
/// scale is a clip, latched when the listener hears it and named by what was sounding. Every case
/// runs with a device buffer, so the frame a clip is rendered and the frame it is heard differ.
/// Full scale in a 16-bit test file is 32767/32768: a lone track at it never clips, and two of
/// them crossfaded equal-power sum to about 1.41 mid-fade.</summary>
public sealed class ClipLightTests
{
    private const int Second = 44100;
    private const int Latency = Second / 2;

    private static TransitionSpec Crossfade(double at, double seconds) => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(at), FromEnd: false),
        FadeOutDuration = TimeSpan.FromSeconds(seconds),
        FadeInPoint = new FadePointSpec(TimeSpan.Zero, FromEnd: false),
        FadeInDuration = TimeSpan.FromSeconds(seconds),
        Curve = FadeCurveKind.EqualPower,
    };

    /// <summary>The first rendered frame at or after <paramref name="from"/> with a sample
    /// beyond full scale.</summary>
    private static long FirstClip(RenderedAudio rendered, long from = 0)
    {
        for (var f = from; f < rendered.Frames; f++)
        {
            if (Math.Abs(rendered.Sample(f, 0)) > 1.0 || Math.Abs(rendered.Sample(f, 1)) > 1.0)
            {
                return f;
            }
        }
        throw new InvalidOperationException("Nothing rendered clipped.");
    }

    /// <summary>Plays the device until the listener has heard every frame before
    /// <paramref name="frame"/>, and not it.</summary>
    private static void HearUpTo(TestEngine tc, long frame) =>
        tc.Output.Advance((int)(frame + Latency - tc.Output.FramesRendered));

    /// <summary>A, B and C as tracks: A and B at full scale, C at 0.8 tagged +6 dB, so C alone
    /// plays at 1.6 while ReplayGain is on.</summary>
    private static (Track A, Track B, Track C) Tracks(TestEngine tc, double seconds = 4)
    {
        var a = tc.TrackOf(TestAudio.WriteDcWav("a.wav", seconds, 1.0));
        var b = tc.TrackOf(TestAudio.WriteDcWav("b.wav", seconds, 1.0));
        var c = tc.TrackOf(TestAudio.WriteDcWav("c.wav", 3, 0.8), replayGainDb: 6.0);
        return (a, b, c);
    }

    // ── Detection, the audible latch and attribution ─────────────────────────────────────

    [Fact]
    public void TwoFullScaleTracks_CrossfadedEqualPower_LatchAClip_NamingTheAdjacency_OnlyOnceItIsHeard()
    {
        using var tc = TestEngine.Create(latency: Latency);
        var (a, b, _) = Tracks(tc);
        tc.Engine.AddTracks(new List<Track> { a, b });
        tc.Engine.Author(new Adjacency(a, b), Crossfade(at: 2, seconds: 2));
        tc.Engine.Play();

        tc.Output.Advance(2 * Second + Latency / 2); // the fade has begun rendering, not sounding
        var clip = FirstClip(tc.Rendered);
        Assert.InRange(clip, 2 * Second, 2 * Second + Second / 100);
        Assert.Null(tc.View.Clip);

        HearUpTo(tc, clip);
        Assert.Null(tc.View.Clip);

        tc.Output.Advance(1); // the listener hears the clipped sample
        var expected = new ClipView(1, ClipCause.Of(new Adjacency(a, b)));
        Assert.Equal(expected, tc.View.Clip);

        tc.Output.Advance(Second); // the rest of the fade clips too: it is the same clip
        Assert.Equal(expected, tc.View.Clip);
        Assert.Equal(expected, tc.Sink.Views[^1].Clip); // and the shell was told
    }

    [Fact]
    public void ALaterClip_OnASingleTrack_RaisesTheCountTo2_AndNamesTheTrack()
    {
        using var tc = TestEngine.Create(latency: Latency);
        var (a, b, c) = Tracks(tc);
        tc.Engine.AddTracks(new List<Track> { a, b, c });
        tc.Engine.Author(new Adjacency(a, b), Crossfade(at: 2, seconds: 2));
        tc.Engine.Play();

        HearUpTo(tc, 5 * Second); // A → B heard, then B alone at full scale
        Assert.Equal(new ClipView(1, ClipCause.Of(new Adjacency(a, b))), tc.View.Clip);

        // B ends at 6 s and C starts with a hard cut, at 1.6.
        HearUpTo(tc, 6 * Second);
        Assert.Equal(1, tc.View.Clip!.Count);
        tc.Output.Advance(1);

        Assert.Equal(new ClipView(2, ClipCause.Of(c)), tc.View.Clip);
    }

    [Fact]
    public void ATrackThatClipsThroughout_IsOneClip()
    {
        using var tc = TestEngine.Create(latency: Latency);
        var (_, _, c) = Tracks(tc);
        tc.Engine.AddTracks(new List<Track> { c });
        tc.Engine.Play();

        HearUpTo(tc, 2 * Second);

        Assert.Equal(new ClipView(1, ClipCause.Of(c)), tc.View.Clip);
    }

    [Fact]
    public void ATrackThatClipsAgain_AfterAQuietStretch_HasClippedTwice()
    {
        using var tc = TestEngine.Create(latency: Latency);
        // 0.8 for its first second, 0.3 for two, then 0.8 again: at +6 dB, 1.6, 0.6 and 1.6.
        var path = TestAudio.WriteWav("verses.wav", 5, f => f < Second || f >= 3 * Second ? 0.8 : 0.3);
        var track = tc.TrackOf(path, replayGainDb: 6.0);
        tc.Engine.AddTracks(new List<Track> { track });
        tc.Engine.Play();

        HearUpTo(tc, 3 * Second);
        Assert.Equal(new ClipView(1, ClipCause.Of(track)), tc.View.Clip);
        tc.Output.Advance(1);

        Assert.Equal(new ClipView(2, ClipCause.Of(track)), tc.View.Clip);
    }

    /// <summary>Found by the Looks smoke: a track that jumps from silence to 0.95, resampled from
    /// 44.1 kHz to a 48 kHz device, overshoots full scale for a few samples. The clip is measured
    /// at the mixer's output, after the engine resamples (ADR 0008), so it is a clip.</summary>
    [Fact]
    public void AHardStepResampledTo48kHz_OvershootsFullScale_AndIsAClip()
    {
        var dir = Path.Combine(TestTemp.Root, "engine", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var output = new FakeAudioOutput { SampleRate = 48000, Latency = 24000 };
        using var engine = new PlaybackEngine(
            output, new Fadem.Core.Transitions.JsonTransitionStore(Path.Combine(dir, "t.json")), new RecordingSink())
        {
            StartAddWorker = work => work(),
            StartPrepareWorker = work => work(),
        };
        var step = TestAudio.WriteWav("step.wav", 3, f => f < Second ? 0.0 : 0.95);
        engine.Add(new[] { step }).Wait();
        engine.Play();

        output.Advance(48000 + 24000 / 2); // the step rendered, not yet heard
        Assert.Null(engine.View.Clip);
        output.Advance(48000);

        Assert.Equal(1, engine.View.Clip!.Count);
        Assert.Equal(step, engine.View.Clip.Latest.Track!.FilePath);
    }

    [Fact]
    public void AClipStillInTheDeviceBuffer_WhenStopped_IsNeverHeard_SoNeverLatched()
    {
        using var tc = TestEngine.Create(latency: Latency);
        var (_, _, c) = Tracks(tc);
        tc.Engine.AddTracks(new List<Track> { c });
        tc.Engine.Play();
        tc.Output.Advance(Latency / 2); // C clips from its first sample; none of it heard yet

        tc.Engine.Stop();

        Assert.Null(tc.View.Clip);
        Assert.Null(tc.Sink.Views[^1].Clip);
    }

    [Fact]
    public void WithoutReplayGain_TheTrackPlaysAt0Point8_AndNothingClips()
    {
        using var tc = TestEngine.Create(new() { ReplayGainMode = Fadem.Core.Settings.ReplayGainMode.Off }, latency: Latency);
        var (_, _, c) = Tracks(tc);
        tc.Engine.AddTracks(new List<Track> { c });
        tc.Engine.Play();

        HearUpTo(tc, 2 * Second);

        Assert.Null(tc.View.Clip);
    }

    // ── Clearing ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ClearClip_ClearsIt_ButStopAndClearDoNot()
    {
        using var tc = TestEngine.Create(latency: Latency);
        var (_, _, c) = Tracks(tc);
        tc.Engine.AddTracks(new List<Track> { c });
        tc.Engine.Play();
        HearUpTo(tc, Second);
        var latched = tc.View.Clip;
        Assert.NotNull(latched);

        tc.Engine.Stop();
        Assert.Equal(latched, tc.View.Clip);
        tc.Engine.Clear();
        Assert.Equal(latched, tc.View.Clip);

        tc.Engine.ClearClip();

        Assert.Null(tc.View.Clip);
        Assert.Null(tc.Sink.Views[^1].Clip);
    }

    [Fact]
    public void Stop_KeepsAClipHeardSinceTheLastCommand()
    {
        using var tc = TestEngine.Create(latency: Latency);
        var (_, _, c) = Tracks(tc);
        tc.Engine.AddTracks(new List<Track> { c });
        tc.Engine.Play();
        HearUpTo(tc, 1); // heard by the device's count, not yet observed by any command

        tc.Engine.Stop();

        Assert.Equal(new ClipView(1, ClipCause.Of(c)), tc.View.Clip);
    }

    [Fact]
    public void ClearClip_WhileATrackStillClips_LightsAgain_WhenTheNextClippedSampleIsHeard()
    {
        using var tc = TestEngine.Create(latency: Latency);
        var (_, _, c) = Tracks(tc);
        tc.Engine.AddTracks(new List<Track> { c });
        tc.Engine.Play();
        HearUpTo(tc, Second);

        tc.Engine.ClearClip();
        Assert.Null(tc.View.Clip);
        tc.Output.Advance(Latency + Second / 10);

        Assert.Equal(new ClipView(1, ClipCause.Of(c)), tc.View.Clip);
    }

    [Fact]
    public void ClearClip_WithNothingLatched_PublishesNothing()
    {
        using var tc = TestEngine.Create(latency: Latency);
        var before = tc.View.Version;

        tc.Engine.ClearClip();

        Assert.Equal(before, tc.View.Version);
    }

    // ── Preview ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AClipDuringAPreview_NamesThePreviewedAdjacency()
    {
        using var tc = TestEngine.Create(latency: Latency);
        var (a, b, _) = Tracks(tc, seconds: 12);
        var quiet = tc.TrackOf(TestAudio.WriteDcWav("quiet.wav", 20, 0.2));
        tc.Engine.AddTracks(new List<Track> { quiet, a, b });
        tc.Engine.Play();
        tc.Output.Advance(Second);

        // From 5 s before A's fade-out point at 8 s: the fade starts 5 s into the preview.
        tc.Engine.Preview(new Adjacency(a, b), Crossfade(at: 8, seconds: 2));
        var previewStart = tc.Output.FramesRendered; // the device plays on into the preview
        tc.Output.Advance(5 * Second + Latency / 2);
        var clip = FirstClip(tc.Rendered, previewStart);
        Assert.InRange(clip, previewStart + 5 * Second, previewStart + 5 * Second + Second / 100);

        HearUpTo(tc, clip);
        Assert.Null(tc.View.Clip);
        tc.Output.Advance(1);

        Assert.Equal(new ClipView(1, ClipCause.Of(new Adjacency(a, b))), tc.View.Clip);
    }

    [Fact]
    public void AClipTheDeviceDropped_WhenAPreviewHeldThePausedQueue_LightsWhenTheResumedQueueIsHeard()
    {
        using var tc = TestEngine.Create(latency: Latency);
        var (_, _, c) = Tracks(tc);
        var quiet = tc.TrackOf(TestAudio.WriteDcWav("quiet.wav", 8, 0.2));
        tc.Engine.AddTracks(new List<Track> { c, quiet });
        tc.Engine.Play();
        tc.Output.Advance(Latency / 2); // C's first clipped samples, still in the device
        tc.Engine.Pause();

        tc.Engine.Preview(new Adjacency(quiet, quiet), Crossfade(at: 2, seconds: 1)); // drops them
        tc.Output.Advance(Second);
        tc.Engine.StopPreview();
        Assert.Null(tc.View.Clip);

        tc.Engine.Play();
        tc.Output.Advance(Latency + Second / 10);

        Assert.Equal(new ClipView(1, ClipCause.Of(c)), tc.View.Clip);
    }

    [Fact]
    public void AClipInAPreviewsPreRoll_NamesThePreviewsOutgoingTrack()
    {
        using var tc = TestEngine.Create(latency: Latency);
        var (_, b, c) = Tracks(tc);
        tc.Engine.AddTracks(new List<Track> { c, b });

        tc.Engine.Preview(new Adjacency(c, b), Crossfade(at: 2, seconds: 1));
        HearUpTo(tc, Second);

        Assert.Equal(new ClipView(1, ClipCause.Of(c)), tc.View.Clip);
    }
}
