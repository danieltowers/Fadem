using Fadem.Core.Domain;
using Fadem.Core.Playback;
using Fadem.Core.Queue;

namespace Fadem.Core.Tests;

/// <summary>⏭, ⏮, the end of the queue and the adjacency ⏭ would reach, through the seam (build
/// issue 06). Each rule of restage ticket 02's Skip and Previous sections has a test here or,
/// where it already held, in <see cref="SeekSkipSemanticsTests"/> and
/// <see cref="PlaybackEngineTransportTests"/>.</summary>
public sealed class SkipPreviousAndEndOfQueueTests
{
    private const int Second = 44100;
    private const double FA = 1000;
    private const double FB = 2000;

    private static TransitionSpec CrossfadeAt(double point, double duration, double fadeInPoint = 0) => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(point), FromEnd: false),
        FadeOutDuration = TimeSpan.FromSeconds(duration),
        FadeInPoint = new FadePointSpec(TimeSpan.FromSeconds(fadeInPoint), FromEnd: false),
        FadeInDuration = TimeSpan.FromSeconds(duration),
    };

    // ── Skip ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SkipMidTransition_NeverPassesOverTheIncomingTrack_ASecondSkipDoes()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 4, 0.3);
        tc.Add(a, b, c);
        tc.Store.Upsert(a, b, CrossfadeAt(1, 1));
        tc.Engine.Play();
        tc.Output.Advance(Second + Second / 4); // mid-crossfade

        tc.Engine.Next();
        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
        Assert.Null(tc.View.Incoming);

        tc.Engine.Next();
        Assert.Equal(c, tc.View.Attended!.Row.Track.FilePath);
    }

    [Fact]
    public void SkipOutsideATransition_HardCutsToTheNextTrackFromZero_IgnoringItsFadeIn()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, CrossfadeAt(2, 1, fadeInPoint: 1)); // B would enter at 1 s, fading in
        tc.Engine.Play();
        tc.Output.Advance(Second / 2);

        tc.Engine.Next();
        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(Playhead.Zero, tc.Playhead);
        tc.Output.Advance(Second / 10);

        Assert.Equal(0.1, tc.Playhead.TotalSeconds, 3);
        Assert.Equal(0.2, tc.Rendered.DcLevel(Second / 2 + 256), 3); // full from its first frame
    }

    [Fact]
    public void SkipOnTheLastRow_WithRepeatOff_Stops()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 2, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 2, 0.2);
        tc.Add(a, b);
        tc.Engine.PlayRow(tc.RowOf(b));
        tc.Output.Advance(Second / 2);

        tc.Engine.Next();

        Assert.Equal(Transport.Stopped, tc.View.Transport);
        Assert.Equal(1, tc.PlaybackFinishedCount);
    }

    [Fact]
    public void SkipOnTheLastRow_UnderRepeatAll_WrapsToTheFirst()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 2, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 2, 0.2);
        tc.Add(a, b);
        tc.Engine.SetRepeat(RepeatMode.All);
        tc.Engine.PlayRow(tc.RowOf(b));
        tc.Output.Advance(Second / 2);

        tc.Engine.Next();

        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(Playhead.Zero, tc.Playhead);
    }

    [Fact]
    public void SkipUnderRepeatOne_MovesToTheNextRow_WhichThenLoops()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 1, 0.3);
        tc.Add(a, b, c);
        tc.Engine.SetRepeat(RepeatMode.One);
        tc.Engine.Play();
        tc.Output.Advance(Second / 4);

        tc.Engine.Next();
        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(RepeatMode.One, tc.View.Repeat);

        tc.Output.Advance(Second + Second / 2); // B ends at 1.25 s and loops: 0.5 s into its second run

        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(0.5, tc.Playhead.TotalSeconds, 3);
        Assert.Equal(new[] { a, b }, tc.AttendedChanges.Select(t => t.FilePath));
    }

    [Fact]
    public void SkipWhilePaused_LandsOnTheNextTrackAtZero_AndStaysPaused()
    {
        using var tc = TestEngine.Create(latency: 4410); // the device holds a block not yet heard
        var a = TestAudio.WriteDcWav("a.wav", 2, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 2, 0.2);
        tc.Add(a, b);
        tc.Engine.Play();
        tc.Output.Advance(Second / 2);
        tc.Engine.Pause();

        tc.Engine.Next();

        Assert.Equal(Transport.Paused, tc.View.Transport);
        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(Playhead.Zero, tc.Playhead);
    }

    // ── Previous ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PreviousAtTheHead_RestartsTheTrack()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        tc.Add(a, b);
        tc.Engine.Play();
        tc.Output.Advance(Second);

        tc.Engine.Previous();

        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(Playhead.Zero, tc.Playhead);
    }

    [Fact]
    public void PreviousAtTheHead_UnderRepeatAll_WrapsToTheLastRow()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 4, 0.3);
        tc.Add(a, b, c);
        tc.Engine.SetRepeat(RepeatMode.All);
        tc.Engine.Play();
        tc.Output.Advance(Second);

        tc.Engine.Previous();

        Assert.Equal(c, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(Playhead.Zero, tc.Playhead);
    }

    [Fact]
    public void Previous_HardCutsToThePreviousRowFromZero_NeverHonouringAReverseAdjacency()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        tc.Add(a, b);
        tc.Store.Upsert(b, a, CrossfadeAt(0.5, 1, fadeInPoint: 1)); // B → A is for playing B then A
        tc.Engine.PlayRow(tc.RowOf(b));
        tc.Output.Advance(Second);

        tc.Engine.Previous();
        tc.Output.Advance(Second / 10);

        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath);
        Assert.Null(tc.View.Incoming);
        Assert.Equal(0.1, tc.Playhead.TotalSeconds, 3);
        Assert.Equal(0.4, tc.Rendered.DcLevel(Second + 256), 3); // A alone, full, from its start
    }

    [Fact]
    public void PreviousDuringATransition_ActsOnTheOutgoingVoice()
    {
        // X A B: mid A → B, the listener is on A, so ⏮ goes to the row before A, not before B.
        using var tc = TestEngine.Create();
        var x = TestAudio.WriteDcWav("x.wav", 4, 0.1);
        var a = TestAudio.WriteToneWav("a.wav", 4, FA);
        var b = TestAudio.WriteToneWav("b.wav", 4, FB);
        tc.Add(x, a, b);
        tc.Store.Upsert(a, b, CrossfadeAt(1, 1));
        tc.Engine.PlayRow(tc.RowOf(a));
        tc.Output.Advance(Second + Second / 4);
        Assert.NotNull(tc.View.Incoming);

        tc.Engine.Previous();

        Assert.Equal(x, tc.View.Attended!.Row.Track.FilePath);
        Assert.Null(tc.View.Incoming);
    }

    [Fact]
    public void PreviousPastThreeSecondsDuringATransition_RestartsTheOutgoingTrack_CancellingIt()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 5, FA);
        var b = TestAudio.WriteToneWav("b.wav", 4, FB);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, CrossfadeAt(3.5, 1));
        tc.Engine.Play();
        tc.Output.Advance(Second * 4); // 0.5 s into the crossfade

        tc.Engine.Previous();
        tc.Output.Advance(Second / 4);

        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath);
        Assert.Null(tc.View.Incoming);
        Assert.Equal(0.25, tc.Playhead.TotalSeconds, 3);
        var end = Second * 4 + Second / 4;
        Assert.InRange(tc.Rendered.ToneAmplitude(FA, end) / 0.4, 0.95, 1.05);
        Assert.True(tc.Rendered.ToneAmplitude(FB, end) < 0.05, "the incoming voice must be gone");
    }

    // ── The end of the queue ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheLastTrack_PlaysToItsLengthWithNoFade_EvenWithAnAuthoredFadeOut_ThenStops()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 4, FA);
        var b = TestAudio.WriteToneWav("b.wav", 4, FB);
        tc.Add(a); // B is not queued: A → B does not fire, and nothing fades A out instead
        tc.Store.Upsert(a, b, CrossfadeAt(2, 1));
        tc.Engine.Play();
        tc.Output.Advance(Second * 5);

        Assert.Equal(0.4, tc.Rendered.ToneAmplitude(FA, Second * 5 / 2), 2);
        Assert.Equal(0.4, tc.Rendered.ToneAmplitude(FA, Second * 3), 2);
        Assert.Equal(0.4, tc.Rendered.ToneAmplitude(FA, Second * 4), 2); // its very last frames
        Assert.Equal(1, tc.PlaybackFinishedCount);
        Assert.Equal(Transport.Stopped, tc.View.Transport);
    }

    [Fact]
    public void Stopped_FollowsThePlayhead_TheDeviceEmitsTheLastFrameBeforeItStops()
    {
        const int latency = 4410 * 2;
        using var tc = TestEngine.Create(latency: latency);
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        tc.Play(a);

        tc.Output.Advance(Second); // A's last frame is rendered, but the device still holds it
        Assert.Equal(Transport.Playing, tc.View.Transport);
        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath);
        Assert.Empty(tc.Output.EmittedAtStop);

        tc.Output.Advance(latency); // the device has now emitted it
        Assert.Equal(Transport.Playing, tc.View.Transport);
        Assert.Equal(1.0, tc.Playhead.TotalSeconds, 3); // the tail, heard to its end
        Assert.Empty(tc.Output.EmittedAtStop);

        tc.Output.Advance(4410);
        Assert.Equal(Transport.Stopped, tc.View.Transport);
        Assert.Equal(1, tc.PlaybackFinishedCount);
        var emitted = Assert.Single(tc.Output.EmittedAtStop);
        Assert.True(emitted >= Second, $"the device stopped with {Second - emitted} frames unheard");
    }

    [Fact]
    public void PlayDuringTheLastTracksTail_DoesNotRestartIt()
    {
        using var tc = TestEngine.Create(latency: 4410 * 2);
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        tc.Play(a);
        tc.Output.Advance(Second);

        tc.Engine.Play();
        tc.Output.Advance(4410 * 3);

        Assert.Equal(Transport.Stopped, tc.View.Transport);
        Assert.Equal(1, tc.PlaybackFinishedCount);
    }

    [Fact]
    public void PreviousDuringTheLastTracksTail_RestartsIt()
    {
        using var tc = TestEngine.Create(latency: 4410 * 2);
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        tc.Play(a);
        tc.Output.Advance(Second * 4);

        tc.Engine.Previous();
        tc.Output.Advance(4410 * 3);

        Assert.Equal(Transport.Playing, tc.View.Transport);
        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(0.1, tc.Playhead.TotalSeconds, 3); // 0.3 s rendered, 0.2 s still in the device
    }

    // ── The adjacency ⏭ would reach ──────────────────────────────────────────────────────

    [Fact]
    public void NextAdjacency_IsTheCurrentRowIntoTheNextRow()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 2, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 2, 0.2);
        tc.Add(a, b);

        Assert.Equal(tc.AdjacencyOf(a, b), tc.View.NextAdjacency); // stopped: from the current row
        tc.Engine.Play();
        Assert.Equal(tc.AdjacencyOf(a, b), tc.View.NextAdjacency);
    }

    [Fact]
    public void NextAdjacency_IsNone_OnTheLastRowWithRepeatOff()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 2, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 2, 0.2);
        tc.Add(a, b);
        tc.Engine.PlayRow(tc.RowOf(b));

        Assert.Null(tc.View.NextAdjacency);
    }

    [Fact]
    public void NextAdjacency_UnderRepeatAll_WrapsFromTheLastRowToTheFirst()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 2, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 2, 0.2);
        tc.Add(a, b);
        tc.Engine.PlayRow(tc.RowOf(b));

        tc.Engine.SetRepeat(RepeatMode.All);

        Assert.Equal(tc.AdjacencyOf(b, a), tc.View.NextAdjacency);
    }

    [Fact]
    public void NextAdjacency_UnderRepeatOne_IsTheNextRow_NotTheRowItself()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 2, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 2, 0.2);
        tc.Add(a, b);
        tc.Engine.Play();

        tc.Engine.SetRepeat(RepeatMode.One);

        Assert.Equal(tc.AdjacencyOf(a, b), tc.View.NextAdjacency);
    }

    [Fact]
    public void NextAdjacency_FollowsTheListener_ItMovesOnAtTheAudibleHandover()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 4, 0.3);
        tc.Add(a, b, c);
        tc.Store.Upsert(a, b, CrossfadeAt(1, 1));
        tc.Engine.Play();

        tc.Output.Advance(Second + Second / 4); // mid A → B: the listener is still on A
        Assert.Equal(tc.AdjacencyOf(a, b), tc.View.NextAdjacency);

        tc.Output.Advance(Second); // A has fallen silent
        Assert.Equal(tc.AdjacencyOf(b, c), tc.View.NextAdjacency);
    }
}
