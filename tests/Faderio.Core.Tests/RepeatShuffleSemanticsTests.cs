using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Queue;

namespace Faderio.Core.Tests;

/// <summary>Repeat and shuffle semantics through the seam (issue 0008).</summary>
public sealed class RepeatShuffleSemanticsTests
{
    private const double FA = 1000;
    private const double FB = 2000;

    [Fact]
    public void RepeatAll_TreatsTheWrapAsARealAdjacency()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 3, FA);
        var b = TestAudio.WriteToneWav("b.wav", 3, FB);
        tc.SetQueueOf(a, b);
        tc.Engine.Repeat = RepeatMode.All;
        tc.Store.Upsert(b, a, new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(0.5),
            FadeInPoint = new FadePointSpec(TimeSpan.FromSeconds(0.5), FromEnd: false),
            FadeInDuration = TimeSpan.FromSeconds(0.5),
        });

        tc.Engine.Play();
        tc.Output.Advance(44100 * 4 + 22050); // 4.5 s: B started at 3.0 s, wrap fade began at 4.0 s

        // The wrap crossfade is in flight at 4.25 s: B fading out, A fading in from 0.5 s.
        var gB = tc.Rendered.ToneAmplitude(FB, 187125) / 0.4;
        var gA = tc.Rendered.ToneAmplitude(FA, 187125) / 0.4;
        Assert.InRange(gB, 0.6, 0.8);  // cos(π/4)
        Assert.InRange(gA, 0.6, 0.8);  // sin(π/4)
        Assert.Equal(0, tc.PlaybackFinishedCount);
    }

    [Fact]
    public void RepeatOne_FiresAnAuthoredSelfAdjacency()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        tc.SetQueueOf(a);
        tc.Engine.Repeat = RepeatMode.One;
        tc.Store.Upsert(a, a, new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(0.5),
            FadeInPoint = new FadePointSpec(TimeSpan.FromSeconds(0.5), FromEnd: false),
            FadeInDuration = TimeSpan.FromSeconds(0.5),
        });

        tc.Engine.Play();
        tc.Output.Advance(44100 + 11025);

        // The song crossfades with itself: outgoing at cos(π/4), incoming at sin(π/4).
        Assert.InRange(tc.Rendered.DcLevel(55125), 0.5, 0.62);

        // It loops forever without finishing.
        tc.Output.Advance(44100 * 6);
        Assert.Equal(0, tc.PlaybackFinishedCount);
    }

    [Fact]
    public void RepeatOne_WithoutAnAuthoredSelfAdjacency_PlainlyRestarts()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        tc.SetQueueOf(a);
        tc.Engine.Repeat = RepeatMode.One;

        tc.Engine.Play();
        tc.Output.Advance(44100 * 3 + 4410);

        // No dip, no fade: the track restarted at full volume at 3.0 s.
        Assert.Equal(0.4, tc.Rendered.Sample(132300, 0), 2);
        Assert.Equal(0, tc.PlaybackFinishedCount);
    }

    [Fact]
    public void Shuffle_FiresAuthoredTransitions_PerAdjacency()
    {
        const int seed = 20260919;
        using var tc = TestEngine.Create(rngSeed: seed);
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 3, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 3, 0.3);

        // Derive the deterministic shuffle the engine's queue will produce.
        var expected = new Faderio.Core.Queue.PlaybackQueue(new Random(seed));
        expected.SetTracks(new[]
        {
            new Track { FilePath = a }, new Track { FilePath = b }, new Track { FilePath = c },
        });
        expected.ShuffleUpcoming();
        var order = expected.Tracks.Select(t => t.FilePath).ToList();

        // Author a transition for whatever pair the shuffle makes adjacent first.
        tc.Store.Upsert(order[0], order[1], new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(0.5),
            FadeInPoint = TimeSpan.Zero,
            FadeInDuration = TimeSpan.FromSeconds(0.5),
        });

        tc.SetQueueOf(a, b, c);
        tc.Engine.Shuffle = true;
        tc.Engine.Play();
        tc.Output.Advance(44100 * 2 + 22050); // 2.5 s: the authored handoff has fired

        Assert.Equal(order[1], tc.Engine.CurrentTrack!.FilePath);
        // The fade fired mid-transition even though the order came from a shuffle:
        // at 1.25 s the outgoing track is neither flat (its level) nor silent.
        var midFadeFrame = (long)(1.25 * 44100);
        var outgoingLevel = tc.Rendered.DcLevel(midFadeFrame);
        Assert.NotEqual(0.4, outgoingLevel, 1);
        Assert.NotEqual(0.0, outgoingLevel, 2);
    }
}
