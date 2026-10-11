using Fadem.Core.Domain;
using Fadem.Core.Playback;
using Fadem.Core.Queue;

namespace Fadem.Core.Tests;

/// <summary>Capture reads the open adjacency's own tracks (phase 6 issue 06): the fade-out point
/// its outgoing track's voice, the fade-in point its incoming track's, never merely the attended
/// voice. <c>CanCapture</c> says exactly when <c>Capture</c> would find one.</summary>
public sealed class CaptureOwnTracksTests
{
    private const int Second = 44100;

    private static TransitionSpec Fade(double fadeOutPoint, double fadeOut, double fadeInPoint, double fadeIn) => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(fadeOutPoint), FromEnd: false),
        FadeOutDuration = TimeSpan.FromSeconds(fadeOut),
        FadeInPoint = new FadePointSpec(TimeSpan.FromSeconds(fadeInPoint), FromEnd: false),
        FadeInDuration = TimeSpan.FromSeconds(fadeIn),
        Curve = FadeCurveKind.Linear,
    };

    /// <summary>A queue of A, B and C, 20 s each, not yet playing.</summary>
    private static (string A, string B, string C) Queue(TestEngine tc)
    {
        var a = TestAudio.WriteDcWav("own-a.wav", 20, 0.1);
        var b = TestAudio.WriteDcWav("own-b.wav", 20, 0.2);
        var c = TestAudio.WriteDcWav("own-c.wav", 20, 0.3);
        tc.Add(a, b, c);
        return (a, b, c);
    }

    [Fact]
    public void AnotherTrackPlaying_CapturesNothing_AndSaysWhichTrackIsNotPlaying()
    {
        using var tc = TestEngine.Create();
        var (a, b, c) = Queue(tc);
        tc.Engine.Play();
        tc.Output.Advance(3 * Second);
        var bc = tc.AdjacencyOf(b, c);

        Assert.Null(tc.Engine.Capture(bc, CaptureSide.FadeOut));
        Assert.Contains(tc.Notices, n => n == $"Nothing captured: {Path.GetFileNameWithoutExtension(b)} is not playing.");
        Assert.Null(tc.Engine.Capture(bc, CaptureSide.FadeIn));
        Assert.Contains(tc.Notices, n => n == $"Nothing captured: {Path.GetFileNameWithoutExtension(c)} is not playing.");
        Assert.False(tc.Engine.CanCapture(bc, CaptureSide.FadeOut));
        Assert.False(tc.Engine.CanCapture(bc, CaptureSide.FadeIn));
        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath);
    }

    [Fact]
    public void TheOutgoingTrackAttended_TheFadeOutSideReadsIt_AndTheFadeInSideCannot()
    {
        using var tc = TestEngine.Create();
        var (_, b, c) = Queue(tc);
        tc.Engine.PlayRow(tc.RowOf(b));
        tc.Output.Advance(3 * Second);
        var bc = tc.AdjacencyOf(b, c);

        Assert.Equal(3.0, tc.Engine.Capture(bc, CaptureSide.FadeOut)!.Value.TotalSeconds, 3);
        Assert.True(tc.Engine.CanCapture(bc, CaptureSide.FadeOut));
        Assert.False(tc.Engine.CanCapture(bc, CaptureSide.FadeIn));
        Assert.Null(tc.Engine.Capture(bc, CaptureSide.FadeIn));
    }

    [Fact]
    public void FromEnd_IsMeasuredFromTheMatchedTracksLength()
    {
        using var tc = TestEngine.Create();
        var (_, b, c) = Queue(tc);
        tc.Engine.PlayRow(tc.RowOf(b));
        tc.Output.Advance(3 * Second);

        Assert.Equal(17.0, tc.Engine.Capture(tc.AdjacencyOf(b, c), CaptureSide.FadeOut, fromEnd: true)!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void DuringALiveCrossfade_TheFadeInSideReadsTheIncomingVoice_AndTheFadeOutSideTheOutgoing()
    {
        using var tc = TestEngine.Create();
        var (_, b, c) = Queue(tc);
        var bc = tc.AdjacencyOf(b, c);
        tc.Engine.Author(bc, Fade(4, 4, fadeInPoint: 2, fadeIn: 4));
        tc.Engine.PlayRow(tc.RowOf(b));
        tc.Output.Advance(5 * Second); // 1 s into the crossfade

        Assert.NotNull(tc.View.Incoming);
        Assert.Equal(5.0, tc.Engine.Capture(bc, CaptureSide.FadeOut)!.Value.TotalSeconds, 3);
        Assert.Equal(3.0, tc.Engine.Capture(bc, CaptureSide.FadeIn)!.Value.TotalSeconds, 3);
        Assert.True(tc.Engine.CanCapture(bc, CaptureSide.FadeIn));
    }

    [Fact]
    public void APreviewOfTheAdjacency_LeavesBothSidesLive_WhileItRuns()
    {
        using var tc = TestEngine.Create();
        var (a, b, c) = Queue(tc);
        tc.Engine.Play();
        tc.Output.Advance(3 * Second);
        var bc = tc.AdjacencyOf(b, c);
        tc.Engine.Preview(bc, Fade(12, 2, fadeInPoint: 4, fadeIn: 2));
        tc.Output.Advance(2 * Second); // the incoming voice has not started

        Assert.True(tc.Engine.CanCapture(bc, CaptureSide.FadeOut));
        Assert.True(tc.Engine.CanCapture(bc, CaptureSide.FadeIn));
        Assert.Equal(9.0, tc.Engine.Capture(bc, CaptureSide.FadeOut)!.Value.TotalSeconds, 3);
        Assert.Null(tc.Engine.Capture(bc, CaptureSide.FadeIn));
        Assert.Contains(tc.Notices, n => n.Contains("incoming track has not started yet"));
        // The preview's voices are B → C's only: C → A finds none, the held queue being on A.
        Assert.False(tc.Engine.CanCapture(tc.AdjacencyOf(c, a), CaptureSide.FadeOut));
    }

    [Fact]
    public void RepeatOne_TheTrackIntoItself_BothSidesReadIt()
    {
        using var tc = TestEngine.Create();
        var (a, _, _) = Queue(tc);
        tc.Engine.SetRepeat(RepeatMode.One);
        tc.Engine.Play();
        tc.Output.Advance(3 * Second);
        var aa = tc.Engine.AdjacenciesOf(tc.RowOf(a)).Self!;

        Assert.Equal(3.0, tc.Engine.Capture(aa, CaptureSide.FadeOut)!.Value.TotalSeconds, 3);
        Assert.Equal(3.0, tc.Engine.Capture(aa, CaptureSide.FadeIn)!.Value.TotalSeconds, 3);
        Assert.True(tc.Engine.CanCapture(aa, CaptureSide.FadeOut));
        Assert.True(tc.Engine.CanCapture(aa, CaptureSide.FadeIn));
    }

    [Fact]
    public void TheSameFileTwiceInTheQueue_EitherRowsVoiceIsThatTrack()
    {
        using var tc = TestEngine.Create();
        var (a, b, _) = Queue(tc);
        tc.Add(a); // A, B, C, A
        tc.Engine.PlayRow(tc.View.Queue[3].Id);
        tc.Output.Advance(3 * Second);

        Assert.Equal(3.0, tc.Engine.Capture(tc.AdjacencyOf(a, b), CaptureSide.FadeOut)!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void NothingPlaying_CannotCapture()
    {
        using var tc = TestEngine.Create();
        var (a, b, _) = Queue(tc);

        Assert.False(tc.Engine.CanCapture(tc.AdjacencyOf(a, b), CaptureSide.FadeOut));
        Assert.Null(tc.Engine.Capture(tc.AdjacencyOf(a, b), CaptureSide.FadeOut));
    }
}
