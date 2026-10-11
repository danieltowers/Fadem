using Fadem.Core.Domain;
using Fadem.Core.Playback;
using Fadem.Core.Queue;
using Fadem.Core.Settings;

namespace Fadem.Core.Tests;

/// <summary>Transition now, through the seam (build issue 08): the adjacency ⏭ would reach starts
/// its transition at the playhead. Only the fade-out point moves; everything else plays as
/// authored. The DC levels read the mix directly: with a linear curve, A at 0.4 and B at 0.2 sum
/// to 0.3 halfway through a crossfade.</summary>
public sealed class TransitionNowTests
{
    private const int Second = 44100;

    private static TransitionSpec Fade(
        double fadeOutPoint, double fadeOut, double fadeInPoint, double fadeIn,
        FadeCurveKind curve = FadeCurveKind.Linear) => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(fadeOutPoint), FromEnd: false),
        FadeOutDuration = TimeSpan.FromSeconds(fadeOut),
        FadeInPoint = new FadePointSpec(TimeSpan.FromSeconds(fadeInPoint), FromEnd: false),
        FadeInDuration = TimeSpan.FromSeconds(fadeIn),
        Curve = curve,
    };

    // ── What it plays ────────────────────────────────────────────────────────────────────

    [Fact]
    public void WithAnAuthoredCrossfade_TheFadeStartsAtThePlayhead_AndTheRestPlaysAsAuthored()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 6, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 6, 0.2);
        tc.Add(a, b);
        // Authored at 5.5 s, the 2 s fade-out is clamped to 0.5 s. Moved to 1 s, it is 2 s again.
        tc.Store.Upsert(a, b, Fade(5.5, 2, fadeInPoint: 1, fadeIn: 2));
        tc.Engine.Play();
        tc.Output.Advance(Second);

        tc.Engine.TransitionNow();

        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(b, tc.View.Incoming!.Row.Track.FilePath);
        Assert.Equal(1.0, tc.View.Incoming.Playhead.TotalSeconds, 3); // from its fade-in point

        tc.Output.Advance(Second);
        Assert.Equal(0.3, tc.Rendered.DcLevel(2 * Second), 2); // halfway through both 2 s fades

        tc.Output.Advance(Second + Second / 10);
        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
        Assert.Null(tc.View.Incoming);
        Assert.Equal(3.1, tc.Playhead.TotalSeconds, 2);
        Assert.Equal(0.2, tc.Rendered.DcLevel(3 * Second + Second / 10), 3);
    }

    [Fact]
    public void AFadeOutThatWouldPassTheLength_IsShortened()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, Fade(3.5, 2, fadeInPoint: 0, fadeIn: 1));
        tc.Engine.Play();
        tc.Output.Advance(3 * Second);

        tc.Engine.TransitionNow();
        tc.Output.Advance(Second / 2);

        // Shortened to the 1 s left: A at half gain (0.2), B half faded in (0.1). Unshortened,
        // A would still be at three quarters.
        Assert.Equal(0.3, tc.Rendered.DcLevel(3 * Second + Second / 2), 2);

        tc.Output.Advance(Second / 2 + Second / 10);
        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(1.1, tc.Playhead.TotalSeconds, 2);
    }

    [Fact]
    public void TheNonOverlappingShape_StopsTheOutgoingTrackNow_AndStartsTheIncomingAtItsFadeInPoint()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, Fade(3, 0, fadeInPoint: 1, fadeIn: 1));
        tc.Engine.Play();
        tc.Output.Advance(Second);

        tc.Engine.TransitionNow();

        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
        Assert.Null(tc.View.Incoming);
        Assert.Equal(1.0, tc.Playhead.TotalSeconds, 3);

        tc.Output.Advance(Second / 2);
        Assert.Equal(0.1, tc.Rendered.DcLevel(Second + Second / 2), 2); // B alone, half faded in
        tc.Output.Advance(Second / 2 + Second / 10);
        Assert.Equal(0.2, tc.Rendered.DcLevel(2 * Second + Second / 10), 3);
    }

    [Fact]
    public void AnUnauthoredAdjacency_HardCutsFromZero()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        tc.Add(a, b);
        tc.Engine.Play();
        tc.Output.Advance(Second);

        tc.Engine.TransitionNow();

        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
        Assert.Null(tc.View.Incoming);
        Assert.Equal(Playhead.Zero, tc.Playhead);
        tc.Output.Advance(Second / 10);
        Assert.Equal(0.2, tc.Rendered.DcLevel(Second + Second / 10), 3); // full from its first frame
    }

    [Fact]
    public void AnUnauthoredAdjacency_PlaysTheDefaultCrossfadeFromNow_WhenThatIsOn()
    {
        using var tc = TestEngine.Create(new PlayerSettings
        {
            UnauthoredAdjacency = UnauthoredAdjacency.DefaultCrossfade,
            DefaultCrossfadeSeconds = 1,
        });
        var a = TestAudio.WriteDcWav("a.wav", 6, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 6, 0.2);
        tc.Add(a, b);
        tc.Engine.Play();
        tc.Output.Advance(Second);

        tc.Engine.TransitionNow();

        Assert.Equal(b, tc.View.Incoming!.Row.Track.FilePath);
        Assert.Equal(Playhead.Zero, tc.View.Incoming.Playhead);
        tc.Output.Advance(Second / 2);
        Assert.Equal(0.6 * Math.Sqrt(0.5), tc.Rendered.DcLevel(Second + Second / 2), 2); // equal power, halfway

        tc.Output.Advance(Second / 2 + Second / 10);
        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(1.1, tc.Playhead.TotalSeconds, 2);
    }

    [Fact]
    public void WithDeviceLatency_TheFadeBeginsAfterWhatTheDeviceHolds_AndTheListenerStaysOnTheOutgoingTrackUntilItEnds()
    {
        using var tc = TestEngine.Create(latency: 4410);
        var a = TestAudio.WriteDcWav("a.wav", 6, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 6, 0.2);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, Fade(5, 1, fadeInPoint: 0, fadeIn: 1));
        tc.Engine.Play();
        tc.Output.Advance(Second); // the listener has heard 0.9 s

        tc.Engine.TransitionNow();

        Assert.Equal(0.9, tc.Playhead.TotalSeconds, 3);
        Assert.Equal(0.4, tc.Rendered.DcLevel(Second), 3); // what the device holds is not refaded
        tc.Output.Advance(Second); // the fade ends at 2 s rendered, 1.9 s heard
        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath);
        tc.Output.Advance(Second / 10);
        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
    }

    // ── Which adjacency ──────────────────────────────────────────────────────────────────

    [Fact]
    public void OnTheLastRow_UnderRepeatAll_ItTakesTheWrapIntoTheFirst()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        tc.Add(a, b);
        tc.Store.Upsert(b, a, Fade(3, 1, fadeInPoint: 0.5, fadeIn: 1));
        tc.Engine.SetRepeat(RepeatMode.All);
        tc.Engine.PlayRow(tc.RowOf(b));
        tc.Output.Advance(Second);

        tc.Engine.TransitionNow();

        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(a, tc.View.Incoming!.Row.Track.FilePath);
        Assert.Equal(0.5, tc.View.Incoming.Playhead.TotalSeconds, 3);
    }

    [Fact]
    public void UnderRepeatOne_ItTakesTheNextRow_NotTheSelfAdjacency()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        tc.Add(a, b);
        tc.Store.Upsert(a, a, Fade(3, 1, fadeInPoint: 2, fadeIn: 1)); // the loop's own transition
        tc.Store.Upsert(a, b, Fade(3, 1, fadeInPoint: 0.5, fadeIn: 1));
        tc.Engine.SetRepeat(RepeatMode.One);
        tc.Engine.Play();
        tc.Output.Advance(Second);

        tc.Engine.TransitionNow();

        Assert.Equal(b, tc.View.Incoming!.Row.Track.FilePath);
        Assert.Equal(0.5, tc.View.Incoming.Playhead.TotalSeconds, 3); // A → B's fade-in point

        tc.Output.Advance(3 * Second); // well past A's authored 3 s point: nothing fires again
        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
        Assert.Null(tc.View.Incoming);
        Assert.Equal(new[] { a, b }, tc.AttendedChanges.Select(t => t.FilePath));
    }

    // ── When it does nothing ─────────────────────────────────────────────────────────────

    [Fact]
    public void WhileATransitionIsInFlight_ItDoesNothing()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 4, 0.3);
        tc.Add(a, b, c);
        tc.Store.Upsert(a, b, Fade(1, 1, fadeInPoint: 0, fadeIn: 1));
        tc.Engine.Play();
        tc.Output.Advance(Second + Second / 4); // mid-crossfade
        var before = tc.View;

        tc.Engine.TransitionNow();

        Assert.Equal(before.Version, tc.View.Version);
        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(b, tc.View.Incoming!.Row.Track.FilePath);
    }

    [Fact]
    public void OnTheLastRow_WithRepeatOff_ItDoesNothing()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        tc.Add(a, b);
        tc.Engine.PlayRow(tc.RowOf(b));
        tc.Output.Advance(Second);
        Assert.Null(tc.View.NextAdjacency);
        var before = tc.View.Version;

        tc.Engine.TransitionNow();

        Assert.Equal(before, tc.View.Version);
        Assert.Equal(Transport.Playing, tc.View.Transport);
        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
    }

    [Fact]
    public void WhilePaused_ItDoesNothing()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        tc.Add(a, b);
        tc.Engine.Play();
        tc.Output.Advance(Second);
        tc.Engine.Pause();
        var before = tc.View.Version;

        tc.Engine.TransitionNow();

        Assert.Equal(before, tc.View.Version);
        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath);
        Assert.Null(tc.View.Incoming);
    }

    [Fact]
    public void WhileAHardCutIsStillInTheDevice_TheViewSaysNo_AndItDoesNothing_UntilTheCutIsHeard()
    {
        using var tc = TestEngine.Create(latency: 4410);
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 4, 0.3);
        tc.Add(a, b, c);
        tc.Engine.Play();
        tc.Output.Advance(Second);
        tc.Engine.Next(); // cut to B, which the listener hears one device buffer later

        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath);
        Assert.NotNull(tc.View.NextAdjacency);
        Assert.Null(tc.View.Incoming);
        Assert.False(tc.View.CanTransitionNow);
        var before = tc.View.Version;
        tc.Engine.TransitionNow();
        Assert.Equal(before, tc.View.Version);

        tc.Output.Advance(4410);
        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
        Assert.True(tc.View.CanTransitionNow);
        tc.Engine.TransitionNow(); // unauthored: a hard cut, heard one buffer later
        tc.Output.Advance(4410);
        Assert.Equal(c, tc.View.Attended!.Row.Track.FilePath);
    }

    [Fact]
    public void WhileTheLastTrackIsFinishing_TheViewSaysNo_EvenWithANextAdjacency()
    {
        using var tc = TestEngine.Create(latency: 4410);
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.2);
        tc.Add(a, b);
        tc.Engine.PlayRow(tc.RowOf(b));
        tc.Output.Advance(Second); // B has rendered its last frame; the device still holds it
        tc.Engine.SetRepeat(RepeatMode.All);

        Assert.Equal(Transport.Playing, tc.View.Transport);
        Assert.NotNull(tc.View.NextAdjacency);
        Assert.False(tc.View.CanTransitionNow);
        var before = tc.View.Version;
        tc.Engine.TransitionNow();
        Assert.Equal(before, tc.View.Version);
    }

    [Fact]
    public void TheViewSaysYes_ExactlyWhenPlayingAloneWithANextAdjacency()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, Fade(1, 1, fadeInPoint: 0, fadeIn: 1));
        Assert.False(tc.View.CanTransitionNow); // stopped

        tc.Engine.Play();
        Assert.True(tc.View.CanTransitionNow);
        tc.Engine.Pause();
        Assert.False(tc.View.CanTransitionNow);
        tc.Engine.Play();
        tc.Output.Advance(Second + Second / 4);
        Assert.False(tc.View.CanTransitionNow); // mid-crossfade
        tc.Output.Advance(Second);
        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
        Assert.False(tc.View.CanTransitionNow); // the last row, repeat off
    }

    [Fact]
    public void WhileStopped_ItDoesNothing()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        tc.Add(a, b);
        var before = tc.View.Version;

        tc.Engine.TransitionNow();

        Assert.Equal(before, tc.View.Version);
        Assert.Equal(Transport.Stopped, tc.View.Transport);
    }
}
