using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Queue;

namespace Faderio.Core.Tests;

/// <summary>
/// Interim transport issue 07: each row of the view says whether its this → next, the adjacency
/// the row's editor button opens, has a transition in the store, so the shell draws the button
/// filled or outlined without reading the store. Null where the row has no this → next.
/// </summary>
public sealed class NextAuthoredTests
{
    private const int Second = 44100;

    private static readonly TransitionSpec Crossfade = new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
        FadeOutDuration = TimeSpan.FromSeconds(0.5),
        FadeInPoint = TimeSpan.Zero,
        FadeInDuration = TimeSpan.FromSeconds(0.5),
    };

    private static (string A, string B, string C) ThreeQueued(TestEngine tc)
    {
        var a = TestAudio.WriteDcWav("a.wav", 2, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 2, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 2, 0.3);
        tc.Add(a, b, c);
        return (a, b, c);
    }

    private static bool?[] Marks(TestEngine tc) => tc.View.Queue.Select(r => r.NextAuthored).ToArray();

    [Fact]
    public void NothingAuthored_EveryRowButTheLastIsFalse_AndTheLastHasNone()
    {
        using var tc = TestEngine.Create();
        ThreeQueued(tc);

        Assert.Equal(new bool?[] { false, false, null }, Marks(tc));
    }

    [Fact]
    public void SavingATransition_MarksItsRow_AndPublishesTheMark_AndRemovingItClearsIt()
    {
        using var tc = TestEngine.Create();
        var (_, b, c) = ThreeQueued(tc);

        tc.Engine.Author(tc.AdjacencyOf(b, c), Crossfade);

        Assert.Equal(new bool?[] { false, true, null }, Marks(tc));
        Assert.Equal(true, tc.Sink.Latest!.Queue[1].NextAuthored); // the shell hears of it

        tc.Engine.Unauthor(tc.AdjacencyOf(b, c));

        Assert.Equal(new bool?[] { false, false, null }, Marks(tc));
        Assert.Equal(false, tc.Sink.Latest!.Queue[1].NextAuthored);
    }

    [Fact]
    public void AMarkFollowsTheRows_WhenTheyMove()
    {
        using var tc = TestEngine.Create();
        var (a, b, c) = ThreeQueued(tc);
        tc.Engine.Author(tc.AdjacencyOf(a, b), Crossfade);

        tc.Engine.Move([tc.RowOf(c)], tc.RowOf(b)); // a, c, b

        Assert.Equal(new bool?[] { false, false, null }, Marks(tc));

        tc.Engine.Move([tc.RowOf(c)], null); // a, b, c

        Assert.Equal(new bool?[] { true, false, null }, Marks(tc));
    }

    [Fact]
    public void UnderRepeatAll_TheLastRowsMarkIsTheWrapIntoTheFirst()
    {
        using var tc = TestEngine.Create();
        var (a, _, c) = ThreeQueued(tc);
        tc.Engine.SetRepeat(RepeatMode.All);

        Assert.Equal(false, tc.View.Queue[2].NextAuthored);

        tc.Engine.Author(tc.AdjacencyOf(c, a), Crossfade);

        Assert.Equal(true, tc.View.Queue[2].NextAuthored);
    }

    [Fact]
    public void ARowMarkedUnreadable_IsPassedOver_AsTheEditorPassesOverIt()
    {
        using var tc = TestEngine.Create();
        var (a, b, c) = ThreeQueued(tc);
        tc.Engine.Author(tc.AdjacencyOf(a, c), Crossfade);
        File.Delete(b);
        tc.Engine.Play();
        tc.Output.Advance(Second / 4); // A's boundary tried B and marked it
        Assert.True(tc.View.Queue[1].IsUnreadable);

        Assert.Equal(true, tc.View.Queue[0].NextAuthored);
        Assert.Equal(true, tc.View.Attended!.Row.NextAuthored); // the voice's row says the same
    }
}
