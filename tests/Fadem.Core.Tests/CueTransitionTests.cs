using Fadem.Core.Domain;
using Fadem.Core.Playback;

namespace Fadem.Core.Tests;

/// <summary>Cue transition, through the seam: the playing track jumps to 5 s before its coming
/// transition begins (the fade-out point, or a hard cut's track end) and plays on from there, so
/// the transition is heard for real, into the next track.</summary>
public sealed class CueTransitionTests
{
    private const int Second = 44100;

    private static TransitionSpec Fade(double fadeOutPoint, double fadeOut) => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(fadeOutPoint), FromEnd: false),
        FadeOutDuration = TimeSpan.FromSeconds(fadeOut),
        FadeInPoint = new FadePointSpec(TimeSpan.Zero, FromEnd: false),
        FadeInDuration = TimeSpan.FromSeconds(fadeOut),
        Curve = FadeCurveKind.Linear,
    };

    [Fact]
    public void ItJumpsTo5sBeforeTheFadeOutPoint_AndTheTransitionPlaysThere()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 12, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 6, 0.2);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, Fade(8, 2));
        tc.Engine.Play();
        tc.Output.Advance(Second);

        tc.Engine.CueTransition();

        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(3.0, tc.Playhead.TotalSeconds, 2);
        tc.Output.Advance(5 * Second - Second / 10);
        Assert.Null(tc.View.Incoming); // not yet
        tc.Output.Advance(Second / 5);
        Assert.Equal(b, tc.View.Incoming!.Row.Track.FilePath); // the crossfade, as authored
    }

    [Fact]
    public void AFadeOutPointAlreadyPassed_IsCuedAgain()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 12, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 6, 0.2);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, Fade(8, 2));
        tc.Engine.Play();
        tc.Output.Advance(Second);
        tc.Engine.Seek(Playhead.FromMilliseconds(9000)); // past the point: the fade is not forced

        tc.Engine.CueTransition();
        Assert.Equal(3.0, tc.Playhead.TotalSeconds, 2);
        tc.Output.Advance(5 * Second + Second / 10);

        Assert.Equal(b, tc.View.Incoming!.Row.Track.FilePath);
    }

    [Fact]
    public void AHardCut_IsCued5sBeforeTheTracksEnd()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 12, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 6, 0.2);
        tc.Add(a, b);
        tc.Engine.Play();
        tc.Output.Advance(Second);

        tc.Engine.CueTransition();

        Assert.Equal(7.0, tc.Playhead.TotalSeconds, 2);
        tc.Output.Advance(5 * Second + Second / 10);
        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
    }

    [Fact]
    public void APointLessThan5sIn_IsCuedFromTheStart()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 12, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 6, 0.2);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, Fade(3, 1));
        tc.Engine.Play();
        tc.Output.Advance(Second / 2);

        tc.Engine.CueTransition();

        Assert.Equal(0.0, tc.Playhead.TotalSeconds, 2);
    }

    [Fact]
    public void WhilePaused_ItPlays()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 12, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 6, 0.2);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, Fade(8, 2));
        tc.Engine.Play();
        tc.Output.Advance(Second);
        tc.Engine.Pause();

        tc.Engine.CueTransition();

        Assert.Equal(Transport.Playing, tc.View.Transport);
        Assert.Equal(3.0, tc.Playhead.TotalSeconds, 2);
    }

    [Fact]
    public void TheViewSaysYes_PlayingOrPausedAlone_WithANextAdjacency()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, Fade(1, 1));
        Assert.False(tc.View.CanCueTransition); // stopped
        var before = tc.View.Version;
        tc.Engine.CueTransition();
        Assert.Equal(before, tc.View.Version);

        tc.Engine.Play();
        Assert.True(tc.View.CanCueTransition);
        tc.Engine.Pause();
        Assert.True(tc.View.CanCueTransition);
        tc.Engine.Play();
        tc.Output.Advance(Second + Second / 4);
        Assert.False(tc.View.CanCueTransition); // mid-crossfade
        before = tc.View.Version;
        tc.Engine.CueTransition();
        Assert.Equal(before, tc.View.Version);
        tc.Output.Advance(Second);
        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
        Assert.False(tc.View.CanCueTransition); // the last row, repeat off
    }
}
