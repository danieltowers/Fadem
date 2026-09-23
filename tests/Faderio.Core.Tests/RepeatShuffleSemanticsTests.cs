using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Queue;

namespace Faderio.Core.Tests;

/// <summary>Repeat and shuffle semantics through the seam. Out of the slice (ticket 05) but
/// kept green: the engine still owns them and the shell will get them back at restaging.</summary>
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
        tc.Add(a, b);
        tc.Engine.SetRepeat(RepeatMode.All);
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
        Assert.Equal(RepeatMode.All, tc.View.Repeat);
    }

    [Fact]
    public void RepeatOne_FiresAnAuthoredSelfAdjacency()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        tc.Add(a);
        tc.Engine.SetRepeat(RepeatMode.One);
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
        tc.Add(a);
        tc.Engine.SetRepeat(RepeatMode.One);

        tc.Engine.Play();
        tc.Output.Advance(44100 * 3 + 4410);

        // No dip, no fade: the track restarted at full volume at 3.0 s.
        Assert.Equal(0.4, tc.Rendered.Sample(132300, 0), 2);
        Assert.Equal(0, tc.PlaybackFinishedCount);
    }

    /// <summary>The order the engine's queue will produce for a seed, derived independently.</summary>
    internal static List<string> ExpectedShuffle(int seed, params string[] paths)
    {
        var expected = new PlaybackQueue(new Random(seed));
        expected.SetEntries(paths.Select((p, i) => new QueueEntry(new RowId(i + 1), new Track { FilePath = p })));
        expected.ShuffleUpcoming();
        return expected.Entries.Select(e => e.Track.FilePath).ToList();
    }

    [Fact]
    public void Shuffle_FiresAuthoredTransitions_PerAdjacency()
    {
        const int seed = 20260919;
        using var tc = TestEngine.Create(rngSeed: seed);
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 3, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 3, 0.3);
        var order = ExpectedShuffle(seed, a, b, c);

        // Author a transition for whatever pair the shuffle makes adjacent first.
        tc.Store.Upsert(order[0], order[1], new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(0.5),
            FadeInPoint = TimeSpan.Zero,
            FadeInDuration = TimeSpan.FromSeconds(0.5),
        });

        tc.Add(a, b, c);
        tc.Engine.SetShuffle(true);
        Assert.Equal(order, tc.View.Queue.Select(r => r.Track.FilePath).ToList());
        tc.Engine.Play();
        tc.Output.Advance(44100 * 2 + 22050); // 2.5 s: the authored handoff has fired

        Assert.Equal(order[1], tc.CurrentTrack!.FilePath);
        // The fade fired mid-transition even though the order came from a shuffle:
        // at 1.25 s the outgoing track is neither flat (its level) nor silent.
        var midFadeFrame = (long)(1.25 * 44100);
        var outgoingLevel = tc.Rendered.DcLevel(midFadeFrame);
        Assert.NotEqual(0.4, outgoingLevel, 1);
        Assert.NotEqual(0.0, outgoingLevel, 2);
    }

    [Fact]
    public void ShuffleOnThenOff_RestoresTheOriginalOrder_AndLosesNothing()
    {
        // v2's shuffle-empties-queue bug: rows added past SetQueue were never in the original
        // order. Rows now enter through one door, so the queue cannot vanish.
        using var tc = TestEngine.Create(rngSeed: 1);
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 1, 0.3);
        tc.Add(a, b);
        tc.Add(c);
        var ids = tc.View.Queue.Select(r => r.Id).ToList();

        tc.Engine.SetShuffle(true);
        Assert.True(tc.View.Shuffle);
        Assert.Equal(3, tc.View.Queue.Count);
        tc.Engine.SetShuffle(false);

        Assert.Equal(ids, tc.View.Queue.Select(r => r.Id).ToList());
        Assert.Equal(new[] { a, b, c }, tc.View.Queue.Select(r => r.Track.FilePath).ToArray());
    }
}
