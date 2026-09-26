using Faderio.Core.Domain;
using Faderio.Core.Playback;

namespace Faderio.Core.Tests;

/// <summary>Interim transport issue 05: <c>SeekBy</c> moves the attended voice by an offset from
/// the playhead the listener hears, clamped to the track, and is then any seek (D7).</summary>
public sealed class SeekByTests
{
    private static readonly TimeSpan Five = TimeSpan.FromSeconds(5);

    [Fact]
    public void Forward_ThenBack_MovesThePlayheadFiveSecondsEachWay()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 12, 0.4);
        tc.Play(a);
        tc.Output.Advance(44100);

        tc.Engine.SeekBy(Five);
        Assert.Equal(6.0, tc.Playhead.TotalSeconds, 3);
        tc.Engine.SeekBy(-Five);
        Assert.Equal(1.0, tc.Playhead.TotalSeconds, 3);
    }

    [Fact]
    public void BackFromNearTheStart_StopsAtZero()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 12, 0.4);
        tc.Play(a);
        tc.Output.Advance(44100 * 2);

        tc.Engine.SeekBy(-Five);
        Assert.Equal(Playhead.Zero, tc.Playhead);
    }

    [Fact]
    public void ForwardPastTheEnd_EndsTheTrack_AndTheQueueMovesOn()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 6, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 6, 0.2);
        tc.Play(a, b);
        tc.Output.Advance(44100 * 3);

        tc.Engine.SeekBy(Five);
        tc.Output.Advance(4410);

        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(0.2, tc.Rendered.DcLevel(tc.Rendered.Frames), 2);
    }

    [Fact]
    public void ForwardPastTheFadeOutPoint_IsAnySeek_TheTransitionIsNotForced()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 12, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 12, 0.2);
        tc.Store.Upsert(a, b, new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(4), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(4),
            FadeInPoint = TimeSpan.Zero,
            FadeInDuration = TimeSpan.FromSeconds(4),
        });
        tc.Play(a, b);
        tc.Output.Advance(44100 * 2); // 2 s before the fade-out point

        tc.Engine.SeekBy(Five); // lands at 7 s, three seconds into where the fade would be
        tc.Output.Advance(44100);

        Assert.Null(tc.View.Incoming);
        Assert.Equal(8.0, tc.Playhead.TotalSeconds, 3);
        Assert.Equal(0.4, tc.Rendered.DcLevel(tc.Rendered.Frames), 2); // a flat, no fade
    }

    [Fact]
    public void Paused_TheJumpMovesThePlayhead_AndPlayResumesThere()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 12, 0.4);
        tc.Play(a);
        tc.Output.Advance(44100);
        tc.Engine.Pause();

        tc.Engine.SeekBy(Five);
        Assert.Equal(Transport.Paused, tc.View.Transport);
        Assert.Equal(6.0, tc.Playhead.TotalSeconds, 3);

        tc.Engine.Play();
        tc.Output.Advance(4410);
        Assert.Equal(6.1, tc.Playhead.TotalSeconds, 3);
    }

    [Fact]
    public void Stopped_ItDoesNothing_AndSaysNothing()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 12, 0.4);
        tc.Add(a);
        var notices = tc.Notices.Count;

        tc.Engine.SeekBy(Five);

        Assert.Equal(Transport.Stopped, tc.View.Transport);
        Assert.Null(tc.View.Attended);
        Assert.Equal(notices, tc.Notices.Count);
    }
}
