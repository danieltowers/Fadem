using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Queue;

namespace Faderio.Core.Tests;

/// <summary>
/// Build issue 15. The editor's adjacencies for a right-clicked row come from the engine, so the
/// shell never computes "next" or "previous": <see cref="PlaybackEngine.AdjacenciesOf"/> gives
/// this → next (the adjacency ⏭ would reach from the row), previous → this (the adjacency into
/// it), and this → this under repeat-one. <see cref="PlaybackEngine.IsAuthored"/> is the editor's
/// test for unsaved edits.
/// </summary>
public sealed class EveryAdjacencyAndUnsavedEditsTests
{
    private const int Second = 44100;

    private static (string A, string B, string C) ThreeQueued(TestEngine tc)
    {
        var a = TestAudio.WriteDcWav("a.wav", 2, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 2, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 2, 0.3);
        tc.Add(a, b, c);
        return (a, b, c);
    }

    [Fact]
    public void AMiddleRow_HasTheRowBefore_AndTheRowAfter_AndNoSelfWithRepeatOff()
    {
        using var tc = TestEngine.Create();
        var (a, b, c) = ThreeQueued(tc);

        var targets = tc.Engine.AdjacenciesOf(tc.RowOf(b));

        Assert.Equal(tc.AdjacencyOf(a, b), targets.Previous);
        Assert.Equal(tc.AdjacencyOf(b, c), targets.Next);
        Assert.Null(targets.Self);
    }

    [Fact]
    public void TheLastRow_HasNoNext_WithRepeatOff()
    {
        using var tc = TestEngine.Create();
        var (_, b, c) = ThreeQueued(tc);

        var targets = tc.Engine.AdjacenciesOf(tc.RowOf(c));

        Assert.Null(targets.Next);
        Assert.Equal(tc.AdjacencyOf(b, c), targets.Previous);
    }

    [Fact]
    public void TheFirstRow_HasNoPrevious_WithRepeatOff()
    {
        using var tc = TestEngine.Create();
        var (a, b, _) = ThreeQueued(tc);

        var targets = tc.Engine.AdjacenciesOf(tc.RowOf(a));

        Assert.Null(targets.Previous);
        Assert.Equal(tc.AdjacencyOf(a, b), targets.Next);
    }

    [Fact]
    public void UnderRepeatAll_TheLastRowsNext_AndTheFirstRowsPrevious_AreTheWrap()
    {
        using var tc = TestEngine.Create();
        var (a, _, c) = ThreeQueued(tc);

        tc.Engine.SetRepeat(RepeatMode.All);

        Assert.Equal(tc.AdjacencyOf(c, a), tc.Engine.AdjacenciesOf(tc.RowOf(c)).Next);
        Assert.Equal(tc.AdjacencyOf(c, a), tc.Engine.AdjacenciesOf(tc.RowOf(a)).Previous);
        Assert.Null(tc.Engine.AdjacenciesOf(tc.RowOf(a)).Self);
    }

    [Fact]
    public void UnderRepeatOne_ThereIsASelfAdjacency_AndNextStillMovesOn()
    {
        using var tc = TestEngine.Create();
        var (a, b, c) = ThreeQueued(tc);

        tc.Engine.SetRepeat(RepeatMode.One);

        var targets = tc.Engine.AdjacenciesOf(tc.RowOf(b));
        Assert.Equal(tc.AdjacencyOf(b, b), targets.Self);
        Assert.Equal(tc.AdjacencyOf(b, c), targets.Next); // as ⏭: repeat-one governs only the natural end
        Assert.Equal(tc.AdjacencyOf(a, b), targets.Previous);
        Assert.Null(tc.Engine.AdjacenciesOf(tc.RowOf(c)).Next); // no wrap: repeat-one is not repeat-all
    }

    [Fact]
    public void UnderShuffle_NextAndPrevious_AreTheVisibleNeighbours()
    {
        const int seed = 20260924;
        using var tc = TestEngine.Create(rngSeed: seed);
        ThreeQueued(tc);

        tc.Engine.SetShuffle(true);

        var shown = tc.View.Queue.Select(r => r.Track.FilePath).ToList();
        var middle = tc.Engine.AdjacenciesOf(tc.RowOf(shown[1]));
        Assert.Equal(tc.AdjacencyOf(shown[0], shown[1]), middle.Previous);
        Assert.Equal(tc.AdjacencyOf(shown[1], shown[2]), middle.Next);
    }

    [Fact]
    public void ARowMarkedUnreadable_IsPassedOver_BothWays()
    {
        using var tc = TestEngine.Create();
        var (a, b, c) = ThreeQueued(tc);
        File.Delete(b);
        tc.Engine.Play();
        tc.Output.Advance(Second / 4); // A's boundary tried B and marked it
        Assert.True(tc.View.Queue[1].IsUnreadable);

        Assert.Equal(tc.AdjacencyOf(a, c), tc.Engine.AdjacenciesOf(tc.RowOf(a)).Next);
        Assert.Equal(tc.AdjacencyOf(a, c), tc.Engine.AdjacenciesOf(tc.RowOf(c)).Previous);
    }

    [Fact]
    public void TheCurrentRowsNext_IsTheViewsNextAdjacency()
    {
        using var tc = TestEngine.Create();
        var (a, _, _) = ThreeQueued(tc);
        tc.Engine.Play();

        Assert.Equal(tc.View.NextAdjacency, tc.Engine.AdjacenciesOf(tc.RowOf(a)).Next);
    }

    [Fact]
    public void AStaleRow_HasNoAdjacencies()
    {
        using var tc = TestEngine.Create();
        var (a, _, _) = ThreeQueued(tc);
        var row = tc.RowOf(a);
        tc.Engine.Remove(new[] { row });

        Assert.Equal(RowAdjacencies.None, tc.Engine.AdjacenciesOf(row));
    }

    [Fact]
    public void TheSelfTarget_Authored_FiresAtTheTracksNaturalEnd()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        tc.Add(a);
        tc.Engine.SetRepeat(RepeatMode.One);
        var self = tc.Engine.AdjacenciesOf(tc.RowOf(a)).Self!;
        tc.Engine.Author(self, new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(0.5),
            FadeInPoint = new FadePointSpec(TimeSpan.FromSeconds(0.5), FromEnd: false),
            FadeInDuration = TimeSpan.FromSeconds(0.5),
        });

        tc.Engine.Play();
        tc.Output.Advance(Second + Second / 4);

        // A crossfades with itself at 1.25 s: outgoing at cos(π/4), incoming at sin(π/4).
        Assert.InRange(tc.Rendered.DcLevel(Second + Second / 4), 0.5, 0.62);
        Assert.Equal(0, tc.PlaybackFinishedCount);
    }

    // ── IsAuthored: would saving this draft change the store? ──────────────────────────

    private static TransitionSpec Crossfade(double at) => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(at), FromEnd: false),
        FadeOutDuration = TimeSpan.FromSeconds(0.5),
        FadeInPoint = TimeSpan.Zero,
        FadeInDuration = TimeSpan.FromSeconds(0.5),
    };

    [Fact]
    public void IsAuthored_WhenTheStoreHoldsTheDraft_AndNotWhenAnyPartDiffers()
    {
        using var tc = TestEngine.Create();
        var (a, b, _) = ThreeQueued(tc);
        var adjacency = tc.AdjacencyOf(a, b);
        tc.Engine.Author(adjacency, Crossfade(1) with { Curve = FadeCurveKind.EqualPower });

        Assert.True(tc.Engine.IsAuthored(adjacency, Crossfade(1) with { Curve = FadeCurveKind.EqualPower }));
        Assert.False(tc.Engine.IsAuthored(adjacency, Crossfade(1.25)));
        Assert.False(tc.Engine.IsAuthored(adjacency, Crossfade(1) with { Curve = FadeCurveKind.Linear }));
        Assert.False(tc.Engine.IsAuthored(adjacency, Crossfade(1) with
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: true),
        }));
        Assert.False(tc.Engine.IsAuthored(adjacency, new TransitionSpec())); // a blank save would remove it
    }

    [Fact]
    public void IsAuthored_AMissingCurve_IsEqualPower()
    {
        using var tc = TestEngine.Create();
        var (a, b, _) = ThreeQueued(tc);
        var adjacency = tc.AdjacencyOf(a, b);
        tc.Store.Upsert(a, b, Crossfade(1)); // no curve, as an older build wrote it

        Assert.True(tc.Engine.IsAuthored(adjacency, Crossfade(1) with { Curve = FadeCurveKind.EqualPower }));
    }

    [Fact]
    public void IsAuthored_WithNothingStored_OnlyForABlankDraft()
    {
        using var tc = TestEngine.Create();
        var (a, b, _) = ThreeQueued(tc);
        var adjacency = tc.AdjacencyOf(a, b);

        Assert.True(tc.Engine.IsAuthored(adjacency, new TransitionSpec { Curve = FadeCurveKind.Linear }));
        Assert.False(tc.Engine.IsAuthored(adjacency, Crossfade(1)));

        tc.Store.Upsert(a, b, new TransitionSpec { Curve = FadeCurveKind.Linear }); // a blank entry, from before issue 14
        Assert.True(tc.Engine.IsAuthored(adjacency, new TransitionSpec()));
    }
}
