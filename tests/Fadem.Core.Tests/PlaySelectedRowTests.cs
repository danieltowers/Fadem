using Fadem.Core.Domain;
using Fadem.Core.Playback;
using Fadem.Core.Transitions;

namespace Fadem.Core.Tests;

/// <summary>Interim transport issue 02: ▶ carries the selected row. A row other than the
/// attended one plays from its start; the attended row, no row or a stale row resumes.</summary>
public sealed class PlaySelectedRowTests
{
    private static TransitionSpec Fade(double fadeOutPoint, double fadeOut, double fadeInPoint, double fadeIn) => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(fadeOutPoint), FromEnd: false),
        FadeOutDuration = TimeSpan.FromSeconds(fadeOut),
        FadeInPoint = new FadePointSpec(TimeSpan.FromSeconds(fadeInPoint), FromEnd: false),
        FadeInDuration = TimeSpan.FromSeconds(fadeIn),
        Curve = FadeCurveKind.Linear,
    };

    [Fact]
    public void Paused_PlayWithAnotherRow_PlaysThatRowFromItsStart()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 3, 0.2);
        tc.Play(a, b);
        tc.Output.Advance(44100);
        tc.Engine.Pause();

        tc.Engine.Play(tc.RowOf(b));
        tc.Output.Advance(4410);

        Assert.Equal(Transport.Playing, tc.View.Transport);
        Assert.Equal(b, tc.CurrentTrack!.FilePath);
        Assert.Equal(0.1, tc.Playhead.TotalSeconds, 3);
        Assert.Equal(0.2, tc.Rendered.DcLevel(tc.Rendered.Frames), 2);
    }

    [Fact]
    public void Paused_PlayWithTheAttendedRowOrNone_Resumes()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 3, 0.2);
        tc.Play(a, b);
        tc.Output.Advance(44100);
        tc.Engine.Pause();

        tc.Engine.Play(tc.RowOf(a));
        tc.Output.Advance(4410);
        Assert.Equal(a, tc.CurrentTrack!.FilePath);
        Assert.Equal(1.1, tc.Playhead.TotalSeconds, 3);

        tc.Engine.Pause();
        tc.Engine.Play(null);
        tc.Output.Advance(4410);
        Assert.Equal(a, tc.CurrentTrack!.FilePath);
        Assert.Equal(1.2, tc.Playhead.TotalSeconds, 3);
    }

    [Fact]
    public void Paused_PlayWithAStaleRow_ResumesWithoutANotice()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        tc.Play(a);
        tc.Output.Advance(44100);
        tc.Engine.Pause();
        var notices = tc.Notices.Count;

        tc.Engine.Play(new RowId(999));
        tc.Output.Advance(4410);

        Assert.Equal(Transport.Playing, tc.View.Transport);
        Assert.Equal(1.1, tc.Playhead.TotalSeconds, 3);
        Assert.Equal(notices, tc.Notices.Count);
    }

    [Fact]
    public void AtLaunch_PlayWithARow_PlaysThatRow()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 3, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 3, 0.1);
        tc.Add(a, b, c);

        tc.Engine.Play(tc.RowOf(c));
        tc.Output.Advance(4410);

        Assert.Equal(c, tc.CurrentTrack!.FilePath);
        Assert.Equal(0.1, tc.Rendered.DcLevel(4410), 2);
    }

    [Fact]
    public void AfterTheQueueEnds_PlayWithARow_PlaysThatRow()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.2);
        tc.Play(a, b);
        tc.Output.Advance(44100 * 3);
        Assert.Equal(Transport.Stopped, tc.View.Transport);

        // b is the marked row (the last one played), and plain Play would restart from the
        // top: an explicit row wins over that, so a stopped Play never ignores the selection.
        tc.Engine.Play(tc.RowOf(b));
        tc.Output.Advance(4410);

        Assert.Equal(b, tc.CurrentTrack!.FilePath);
        Assert.Equal(0.1, tc.Playhead.TotalSeconds, 3);
    }

    [Fact]
    public void MidCrossfade_PausedThenPlayWithAnotherRow_AbandonsTheCrossfade()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        var d = TestAudio.WriteDcWav("d.wav", 4, 0.1);
        tc.Store.Upsert(a, b, Fade(2, 2, fadeInPoint: 0, fadeIn: 2));
        tc.Play(a, b, d);
        tc.Output.Advance((int)(44100 * 2.5)); // half a second into the overlap
        Assert.NotNull(tc.View.Incoming);
        tc.Engine.Pause();

        tc.Engine.Play(tc.RowOf(d));
        tc.Output.Advance(4410);

        Assert.Equal(d, tc.CurrentTrack!.FilePath);
        Assert.Null(tc.View.Incoming);
        Assert.Equal(0.1, tc.Rendered.DcLevel(tc.Rendered.Frames), 2); // d alone, at full volume
    }

    [Fact]
    public void TogglePlayPause_PassesTheRowOnlyWhenItPlays()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 3, 0.2);
        tc.Play(a, b);
        tc.Output.Advance(44100);

        tc.Engine.TogglePlayPause(tc.RowOf(b)); // playing: pauses, the row is ignored
        Assert.Equal(Transport.Paused, tc.View.Transport);
        Assert.Equal(a, tc.CurrentTrack!.FilePath);

        tc.Engine.TogglePlayPause(tc.RowOf(b)); // paused: plays b
        tc.Output.Advance(4410);
        Assert.Equal(b, tc.CurrentTrack!.FilePath);
        Assert.Equal(0.1, tc.Playhead.TotalSeconds, 3);
    }
}
