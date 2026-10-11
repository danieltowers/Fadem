using Fadem.Core.Domain;
using Fadem.Core.Playback;
using Fadem.Core.Queue;

namespace Fadem.Core.Tests;

/// <summary>Which of an adjacency's tracks the listener has in hand (phase 6 issue 07): the
/// editor's traffic light. The outgoing track wins over the incoming one; a track of neither is
/// another track; stopped is nothing.</summary>
public sealed class AdjacencyHeardTests
{
    private const int Second = 44100;

    /// <summary>A queue of A, B and C, 20 s each, not yet playing.</summary>
    private static (string A, string B, string C) Queue(TestEngine tc)
    {
        var a = TestAudio.WriteDcWav("heard-a.wav", 20, 0.1);
        var b = TestAudio.WriteDcWav("heard-b.wav", 20, 0.2);
        var c = TestAudio.WriteDcWav("heard-c.wav", 20, 0.3);
        tc.Add(a, b, c);
        return (a, b, c);
    }

    private static TransitionSpec Fade(double fadeOutPoint, double fadeOut, double fadeInPoint, double fadeIn) => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(fadeOutPoint), FromEnd: false),
        FadeOutDuration = TimeSpan.FromSeconds(fadeOut),
        FadeInPoint = new FadePointSpec(TimeSpan.FromSeconds(fadeInPoint), FromEnd: false),
        FadeInDuration = TimeSpan.FromSeconds(fadeIn),
        Curve = FadeCurveKind.Linear,
    };

    [Fact]
    public void Stopped_IsNothing()
    {
        using var tc = TestEngine.Create();
        var (_, b, c) = Queue(tc);

        Assert.Equal(AdjacencyHeard.Nothing, tc.Engine.Heard(tc.AdjacencyOf(b, c)));
    }

    [Fact]
    public void ATrackOfNeitherSidePlaying_IsAnotherTrack()
    {
        using var tc = TestEngine.Create();
        var (_, b, c) = Queue(tc);
        tc.Engine.Play();
        tc.Output.Advance(2 * Second);

        Assert.Equal(AdjacencyHeard.OtherTrack, tc.Engine.Heard(tc.AdjacencyOf(b, c)));
    }

    [Fact]
    public void TheOutgoingTrackPlaying_OrPaused_IsOutgoing()
    {
        using var tc = TestEngine.Create();
        var (_, b, c) = Queue(tc);
        tc.Engine.PlayRow(tc.RowOf(b));
        tc.Output.Advance(2 * Second);

        Assert.Equal(AdjacencyHeard.Outgoing, tc.Engine.Heard(tc.AdjacencyOf(b, c)));
        tc.Engine.Pause();
        Assert.Equal(AdjacencyHeard.Outgoing, tc.Engine.Heard(tc.AdjacencyOf(b, c)));
    }

    /// <summary>Round 2 issue 08: the outgoing track heard is green only while its row's next is
    /// the incoming track, the transition about to be heard. Once the queue gives that row another
    /// next, by a removal or a move, the pair is not adjacent: <see cref="AdjacencyHeard.OutgoingIntoAnother"/>.</summary>
    [Fact]
    public void TheOutgoingTrackPlaying_WithItsNextRemoved_IsOutgoingIntoAnother()
    {
        using var tc = TestEngine.Create();
        var (a, b, c) = Queue(tc);
        var ab = tc.AdjacencyOf(a, b);
        tc.Engine.Play();
        tc.Output.Advance(2 * Second);

        tc.Engine.Remove(new[] { tc.RowOf(b) });

        Assert.Equal(AdjacencyHeard.OutgoingIntoAnother, tc.Engine.Heard(ab));
        Assert.Equal(AdjacencyHeard.Outgoing, tc.Engine.Heard(tc.AdjacencyOf(a, c)));
    }

    [Fact]
    public void TheOutgoingTrackPlaying_WithAnotherRowMovedInAsItsNext_IsOutgoingIntoAnother()
    {
        using var tc = TestEngine.Create();
        var (a, b, c) = Queue(tc);
        tc.Engine.Play();
        tc.Output.Advance(2 * Second);

        tc.Engine.Move(new[] { tc.RowOf(c) }, before: tc.RowOf(b));

        Assert.Equal(AdjacencyHeard.OutgoingIntoAnother, tc.Engine.Heard(tc.AdjacencyOf(a, b)));
        Assert.Equal(AdjacencyHeard.Outgoing, tc.Engine.Heard(tc.AdjacencyOf(a, c)));
    }

    [Fact]
    public void TheLastTrackPlaying_WithRepeatOff_IsOutgoingIntoAnother_ForAnyPairItStarts()
    {
        using var tc = TestEngine.Create();
        var (a, _, c) = Queue(tc);
        tc.Engine.PlayRow(tc.RowOf(c));
        tc.Output.Advance(2 * Second);

        Assert.Equal(AdjacencyHeard.OutgoingIntoAnother, tc.Engine.Heard(tc.AdjacencyOf(c, a)));
    }

    [Fact]
    public void TheLastTrackPlaying_UnderRepeatAll_IsOutgoing_IntoTheFirst()
    {
        using var tc = TestEngine.Create();
        var (a, _, c) = Queue(tc);
        tc.Engine.SetRepeat(RepeatMode.All);
        tc.Engine.PlayRow(tc.RowOf(c));
        tc.Output.Advance(2 * Second);

        Assert.Equal(AdjacencyHeard.Outgoing, tc.Engine.Heard(tc.AdjacencyOf(c, a)));
    }

    [Fact]
    public void ATrackPlaying_UnderRepeatOne_IsOutgoing_IntoItself()
    {
        using var tc = TestEngine.Create();
        var (a, _, _) = Queue(tc);
        tc.Engine.SetRepeat(RepeatMode.One);
        tc.Engine.Play();
        tc.Output.Advance(2 * Second);

        Assert.Equal(AdjacencyHeard.Outgoing, tc.Engine.Heard(tc.AdjacencyOf(a, a)));
    }

    [Fact]
    public void TheIncomingTrackPlaying_IsIncoming()
    {
        using var tc = TestEngine.Create();
        var (_, b, c) = Queue(tc);
        tc.Engine.PlayRow(tc.RowOf(c));
        tc.Output.Advance(2 * Second);

        Assert.Equal(AdjacencyHeard.Incoming, tc.Engine.Heard(tc.AdjacencyOf(b, c)));
    }

    [Fact]
    public void ACrossfadeIntoTheOutgoingTrack_IsOutgoing()
    {
        using var tc = TestEngine.Create();
        var (a, b, c) = Queue(tc);
        tc.Engine.Author(tc.AdjacencyOf(a, b), Fade(4, 4, fadeInPoint: 2, fadeIn: 4));
        tc.Engine.Play();
        tc.Output.Advance(5 * Second); // 1 s into the crossfade: A attended, B incoming

        Assert.NotNull(tc.View.Incoming);
        Assert.Equal(AdjacencyHeard.Outgoing, tc.Engine.Heard(tc.AdjacencyOf(b, c)));
        Assert.Equal(AdjacencyHeard.Outgoing, tc.Engine.Heard(tc.AdjacencyOf(a, b))); // both heard: the first wins
    }

    [Fact]
    public void APreview_IsOutgoingInItsPreRoll_AndIncomingInItsTail()
    {
        using var tc = TestEngine.Create();
        var (_, b, c) = Queue(tc);
        tc.Engine.Play();
        tc.Output.Advance(2 * Second);
        var bc = tc.AdjacencyOf(b, c);
        tc.Engine.Preview(bc, Fade(12, 2, fadeInPoint: 4, fadeIn: 2));

        tc.Output.Advance(2 * Second);
        Assert.Equal(AdjacencyHeard.Outgoing, tc.Engine.Heard(bc));

        tc.Output.Advance(6 * Second); // the outgoing voice has fallen silent
        Assert.Equal(AdjacencyHeard.Incoming, tc.Engine.Heard(bc));
    }
}
