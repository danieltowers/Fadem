using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Settings;
using NAudio.Wave;

namespace Faderio.Core.Tests;

/// <summary>Preview, through the seam (build issue 16): a separate pair of preview voices in the
/// main engine plays the editor's draft on the one output, from 5 s before the fade-out point to
/// 5 s after the later fade completes, while the queue is held where it was. One test per rule of
/// the prototype's walkthroughs. The staircase track is 0.05 in its first second, 0.10 in its
/// second and so on, so the rendered level says where in the track the output is.</summary>
public sealed class PreviewTests
{
    private const int Second = 44100;

    private static string Staircase(string name, double seconds) =>
        TestAudio.WriteWav(name, seconds, f => 0.05 * (1 + f / Second));

    private static TransitionSpec Fade(double fadeOutPoint, double fadeOut, double fadeInPoint, double fadeIn) => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(fadeOutPoint), FromEnd: false),
        FadeOutDuration = TimeSpan.FromSeconds(fadeOut),
        FadeInPoint = new FadePointSpec(TimeSpan.FromSeconds(fadeInPoint), FromEnd: false),
        FadeInDuration = TimeSpan.FromSeconds(fadeIn),
        Curve = FadeCurveKind.Linear,
    };

    /// <summary>A playing queue of A (20 s staircase) and B (20 s at 0.3), 3 s into A.</summary>
    private static (string A, string B) QueuePlayingAt3s(TestEngine tc)
    {
        var a = Staircase("a.wav", 20);
        var b = TestAudio.WriteDcWav("b.wav", 20, 0.3);
        tc.Play(a, b);
        tc.Output.Advance(3 * Second);
        return (a, b);
    }

    // ── Happy path ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void APreview_HoldsAPlayingQueue_AndPlaysTheOutgoingTrackFrom5sBeforeItsFadeOutPoint()
    {
        using var tc = TestEngine.Create();
        var (a, b) = QueuePlayingAt3s(tc);

        tc.Engine.Preview(tc.AdjacencyOf(a, b), Fade(12, 2, fadeInPoint: 4, fadeIn: 2));
        var before = tc.Rendered.Frames;
        tc.Output.Advance(Second);

        var view = tc.View;
        Assert.Equal(Transport.Paused, view.Transport);
        Assert.Equal(a, view.Attended!.Row.Track.FilePath);
        Assert.Equal(3.0, view.Attended.Playhead.TotalSeconds, 3);
        Assert.NotNull(view.Preview);
        Assert.Equal(PreviewPhase.PreRoll, view.Preview!.Phase);
        Assert.Equal(8.0, view.Preview.Outgoing!.Value.TotalSeconds, 3);
        Assert.Null(view.Preview.Incoming);
        // A from 7 s: its eighth second is 0.40.
        Assert.Equal(0.40, tc.Rendered.DcLevel(before + Second), 3);
    }

    [Fact]
    public void APreview_RunsThroughItsTransitionAndTail_ThenEnds_AndTheQueueStaysPausedWhereItWasHeld()
    {
        using var tc = TestEngine.Create();
        var (a, b) = QueuePlayingAt3s(tc);
        tc.Engine.Preview(tc.AdjacencyOf(a, b), Fade(12, 2, fadeInPoint: 4, fadeIn: 2));

        tc.Output.Advance(6 * Second); // 1 s into the transition
        var mid = tc.View.Preview!;
        Assert.Equal(PreviewPhase.Transition, mid.Phase);
        Assert.Equal(13.0, mid.Outgoing!.Value.TotalSeconds, 3);
        Assert.Equal(5.0, mid.Incoming!.Value.TotalSeconds, 3);

        tc.Output.Advance(2 * Second); // both fades done at 7 s
        var tail = tc.View.Preview!;
        Assert.Equal(PreviewPhase.Tail, tail.Phase);
        Assert.Null(tail.Outgoing);
        Assert.Equal(7.0, tail.Incoming!.Value.TotalSeconds, 3);
        Assert.Equal(0.3, tc.Rendered.DcLevel(tc.Rendered.Frames), 3); // B alone

        tc.Output.Advance(5 * Second); // 5 s after the later fade: the end

        Assert.Null(tc.View.Preview);
        Assert.Equal(Transport.Stopped, tc.Output.State);
        Assert.Equal(Transport.Paused, tc.View.Transport);
        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(3.0, tc.Playhead.TotalSeconds, 3);
        Assert.Equal(3 * Second + 12 * Second, tc.Rendered.Frames); // the preview ran 12 s, then nothing

        tc.Engine.Play();
        tc.Output.Advance(Second);

        Assert.Equal(0.20, tc.Rendered.DcLevel(tc.Rendered.Frames), 3); // A's fourth second
        Assert.Equal(4.0, tc.Playhead.TotalSeconds, 3);
    }

    // ── Tweak and listen again ───────────────────────────────────────────────────────────

    [Fact]
    public void PreviewAgain_RestartsWithTheCurrentDraft_AndNothingReachesTheStore()
    {
        using var tc = TestEngine.Create();
        var (a, b) = QueuePlayingAt3s(tc);
        var adjacency = tc.AdjacencyOf(a, b);
        tc.Engine.Preview(adjacency, Fade(12, 2, fadeInPoint: 4, fadeIn: 2));
        tc.Output.Advance(3 * Second);

        tc.Engine.Preview(adjacency, Fade(15, 2, fadeInPoint: 4, fadeIn: 2));
        tc.Output.Advance(Second);

        var view = tc.View;
        Assert.Equal(PreviewPhase.PreRoll, view.Preview!.Phase);
        Assert.Equal(11.0, view.Preview.Outgoing!.Value.TotalSeconds, 3); // from 10 s, 5 s before 15 s
        Assert.Equal(0.55, tc.Rendered.DcLevel(tc.Rendered.Frames), 3); // A's eleventh second
        Assert.Equal(Transport.Paused, view.Transport);
        Assert.Equal(3.0, view.Attended!.Playhead.TotalSeconds, 3); // still held where it was
        Assert.Null(tc.Engine.Transition(adjacency));
    }

    [Fact]
    public void Saving_DuringAPreview_WritesTheStore_AndThePreviewPlaysOn()
    {
        using var tc = TestEngine.Create();
        var (a, b) = QueuePlayingAt3s(tc);
        var adjacency = tc.AdjacencyOf(a, b);
        var draft = Fade(12, 2, fadeInPoint: 4, fadeIn: 2);
        tc.Engine.Preview(adjacency, draft);
        tc.Output.Advance(2 * Second);

        tc.Engine.Author(adjacency, draft);
        tc.Output.Advance(Second);

        Assert.Equal(draft, tc.Engine.Transition(adjacency));
        Assert.Equal(10.0, tc.View.Preview!.Outgoing!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void ARunningPreview_KeepsTheDraftItStartedWith()
    {
        using var tc = TestEngine.Create();
        var (a, b) = QueuePlayingAt3s(tc);
        var draft = new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(12), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(2),
        };
        tc.Engine.Preview(tc.AdjacencyOf(a, b), draft);
        // A different draft saved for the same adjacency does not reach the running preview.
        tc.Engine.Author(tc.AdjacencyOf(a, b), Fade(8, 2, fadeInPoint: 0, fadeIn: 2));

        tc.Output.Advance(5 * Second + Second / 2);

        Assert.Equal(PreviewPhase.Transition, tc.View.Preview!.Phase); // still 12 s, not 8 s
        Assert.Equal(12.5, tc.View.Preview.Outgoing!.Value.TotalSeconds, 3);
    }

    // ── Capture while previewing ─────────────────────────────────────────────────────────

    [Fact]
    public void CaptureFadeIn_BeforeTheIncomingVoiceHasStarted_CapturesNothing_AndSaysWhy()
    {
        using var tc = TestEngine.Create();
        var (a, b) = QueuePlayingAt3s(tc);
        tc.Engine.Preview(tc.AdjacencyOf(a, b), Fade(12, 2, fadeInPoint: 4, fadeIn: 2));
        tc.Output.Advance(2 * Second);

        Assert.Null(tc.Engine.Capture(CaptureSide.FadeIn));
        Assert.Contains(tc.Notices, n => n.Contains("incoming track has not started yet"));
    }

    [Fact]
    public void Capture_DuringAPreview_ReadsThePreviewsVoiceForItsSide_NeverTheHeldQueue()
    {
        using var tc = TestEngine.Create();
        var (a, b) = QueuePlayingAt3s(tc);
        tc.Engine.Preview(tc.AdjacencyOf(a, b), Fade(12, 2, fadeInPoint: 4, fadeIn: 2));
        tc.Output.Advance(5 * Second + Second / 2); // half a second into the transition

        Assert.Equal(12.5, tc.Engine.Capture(CaptureSide.FadeOut)!.Value.TotalSeconds, 3);
        Assert.Equal(4.5, tc.Engine.Capture(CaptureSide.FadeIn)!.Value.TotalSeconds, 3);
        // From the end of the previewed outgoing track, 20 s long.
        Assert.Equal(7.5, tc.Engine.Capture(CaptureSide.FadeOut, fromEnd: true)!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void CaptureFadeOut_OnceThePreviewsOutgoingVoiceHasFallenSilent_CapturesNothing_AndSaysWhy()
    {
        using var tc = TestEngine.Create();
        var (a, b) = QueuePlayingAt3s(tc);
        tc.Engine.Preview(tc.AdjacencyOf(a, b), Fade(12, 2, fadeInPoint: 4, fadeIn: 2));
        tc.Output.Advance(8 * Second);

        Assert.Null(tc.Engine.Capture(CaptureSide.FadeOut));
        Assert.Contains(tc.Notices, n => n.Contains("outgoing track has fallen silent"));
    }

    [Fact]
    public void ACapturedFadeIn_PreviewedAgain_IsWhereTheIncomingTrackStarts()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 20, 0.4);
        var b = Staircase("b.wav", 20);
        tc.Play(a, b);
        tc.Output.Advance(3 * Second);
        var adjacency = tc.AdjacencyOf(a, b);
        tc.Engine.Preview(adjacency, Fade(12, 2, fadeInPoint: 4, fadeIn: 2));
        tc.Output.Advance(8 * Second); // B heard at 7 s
        var captured = tc.Engine.Capture(CaptureSide.FadeIn)!.Value;

        tc.Engine.Preview(adjacency, Fade(12, 0, fadeInPoint: captured.TotalSeconds, fadeIn: 0));
        tc.Output.Advance(5 * Second + Second / 2);

        Assert.Equal(7.5, tc.View.Preview!.Incoming!.Value.TotalSeconds, 3);
        Assert.Equal(0.40, tc.Rendered.DcLevel(tc.Rendered.Frames), 3); // B's eighth second
    }

    // ── Queue buttons mid-preview ────────────────────────────────────────────────────────

    /// <summary>A queue playing at 3 s into A, held by a preview that has run 2 s.</summary>
    private static (string A, string B) HeldMidPreview(TestEngine tc)
    {
        var (a, b) = QueuePlayingAt3s(tc);
        tc.Engine.Preview(tc.AdjacencyOf(a, b), Fade(12, 2, fadeInPoint: 4, fadeIn: 2));
        tc.Output.Advance(2 * Second);
        return (a, b);
    }

    [Fact]
    public void Next_DuringAPreview_EndsIt_ThenSkipsTheHeldQueue_WhichStaysPaused()
    {
        using var tc = TestEngine.Create();
        var (_, b) = HeldMidPreview(tc);

        tc.Engine.Next();

        Assert.Null(tc.View.Preview);
        Assert.Equal(Transport.Paused, tc.View.Transport);
        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(0.0, tc.Playhead.TotalSeconds, 3);
    }

    [Fact]
    public void Play_DuringAPreview_EndsIt_ThenPlaysTheQueueFromWhereItWasHeld()
    {
        using var tc = TestEngine.Create();
        HeldMidPreview(tc);

        tc.Engine.TogglePlayPause(); // the button shows ▶: the queue is paused
        tc.Output.Advance(Second);

        Assert.Null(tc.View.Preview);
        Assert.Equal(Transport.Playing, tc.View.Transport);
        Assert.Equal(4.0, tc.Playhead.TotalSeconds, 3);
        Assert.Equal(0.20, tc.Rendered.DcLevel(tc.Rendered.Frames), 3); // A's fourth second
    }

    [Fact]
    public void Pause_DuringAPreview_EndsIt_AndTheQueueStaysPaused()
    {
        using var tc = TestEngine.Create();
        HeldMidPreview(tc);

        tc.Engine.Pause();
        tc.Output.Advance(Second);

        Assert.Null(tc.View.Preview);
        Assert.Equal(Transport.Paused, tc.View.Transport);
        Assert.Equal(3.0, tc.Playhead.TotalSeconds, 3);
        Assert.Equal(Transport.Stopped, tc.Output.State); // the device played the preview, and stops with it
    }

    [Fact]
    public void Seek_DuringAPreview_EndsIt_ThenSeeksTheHeldQueue()
    {
        using var tc = TestEngine.Create();
        HeldMidPreview(tc);

        tc.Engine.Seek(Playhead.FromMilliseconds(10_000));
        tc.Engine.Play();
        tc.Output.Advance(Second);

        Assert.Null(tc.View.Preview);
        Assert.Equal(11.0, tc.Playhead.TotalSeconds, 3);
        Assert.Equal(0.55, tc.Rendered.DcLevel(tc.Rendered.Frames), 3); // A's eleventh second
    }

    [Fact]
    public void StopPreview_EndsIt_AndAPausedQueueStaysPaused_AfterWhatThePausedDeviceStillHeld()
    {
        using var tc = TestEngine.Create(latency: Second / 2);
        var (a, b) = QueuePlayingAt3s(tc); // rendered to 3 s, heard at 2.5 s
        tc.Engine.Pause();

        // The device holding the unheard half second is stopped to play the preview: that half
        // second is dropped, never played before it.
        tc.Engine.Preview(tc.AdjacencyOf(a, b), Fade(12, 2, fadeInPoint: 4, fadeIn: 2));
        tc.Output.Advance(Second);
        Assert.Equal(3.0, tc.Playhead.TotalSeconds, 3);
        Assert.Equal(7.5, tc.View.Preview!.Outgoing!.Value.TotalSeconds, 3);

        tc.Engine.StopPreview();

        Assert.Null(tc.View.Preview);
        Assert.Equal(Transport.Paused, tc.View.Transport);
        Assert.Equal(3.0, tc.Playhead.TotalSeconds, 3);
    }

    [Fact]
    public void APreview_WithTheQueueStopped_LeavesItStopped()
    {
        using var tc = TestEngine.Create();
        var a = Staircase("a.wav", 20);
        var b = TestAudio.WriteDcWav("b.wav", 20, 0.3);
        tc.Add(a, b);

        tc.Engine.Preview(tc.AdjacencyOf(a, b), Fade(12, 2, fadeInPoint: 4, fadeIn: 2));
        tc.Output.Advance(Second);
        Assert.Equal(Transport.Stopped, tc.View.Transport);
        Assert.Equal(PreviewPhase.PreRoll, tc.View.Preview!.Phase);

        tc.Output.Advance(12 * Second);

        Assert.Null(tc.View.Preview);
        Assert.Equal(Transport.Stopped, tc.View.Transport);
        Assert.Null(tc.View.Attended);
    }

    // ── Row removed mid-preview ──────────────────────────────────────────────────────────

    [Fact]
    public void RemovingThePlayingRow_DuringAPreview_DoesNotStopIt_AndTheHeldQueueLandsPausedOnTheNextRow()
    {
        using var tc = TestEngine.Create();
        var (a, b) = HeldMidPreview(tc);

        tc.Engine.Remove([tc.RowOf(a)]);
        tc.Output.Advance(Second);

        var view = tc.View;
        Assert.Equal(PreviewPhase.PreRoll, view.Preview!.Phase);
        Assert.Equal(10.0, view.Preview.Outgoing!.Value.TotalSeconds, 3); // A plays on in the preview
        Assert.Equal(Transport.Paused, view.Transport);
        Assert.Equal(b, view.Attended!.Row.Track.FilePath);
        Assert.Equal(0.0, view.Attended.Playhead.TotalSeconds, 3);

        tc.Output.Advance(10 * Second);

        Assert.Null(tc.View.Preview);
        Assert.Equal(Transport.Paused, tc.View.Transport);
        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
    }

    [Fact]
    public void RemovingEveryRow_DuringAPreview_DoesNotStopIt_AndTheQueueIsStoppedAfterwards()
    {
        using var tc = TestEngine.Create();
        HeldMidPreview(tc);

        tc.Engine.Clear();
        tc.Output.Advance(Second);

        Assert.Equal(10.0, tc.View.Preview!.Outgoing!.Value.TotalSeconds, 3);
        Assert.Equal(Transport.Stopped, tc.View.Transport);

        tc.Output.Advance(10 * Second);

        Assert.Null(tc.View.Preview);
        Assert.Equal(Transport.Stopped, tc.View.Transport);
    }

    // ── Unauthored boundary ──────────────────────────────────────────────────────────────

    [Fact]
    public void AnUnauthoredDraft_PlaysAsAHardCutAtTheOutgoingTracksEnd_With5sEitherSide()
    {
        using var tc = TestEngine.Create();
        var (a, b) = QueuePlayingAt3s(tc);

        tc.Engine.Preview(tc.AdjacencyOf(a, b), new TransitionSpec());
        tc.Output.Advance(Second);
        Assert.Equal(16.0, tc.View.Preview!.Outgoing!.Value.TotalSeconds, 3); // from 15 s of 20 s

        tc.Output.Advance(4 * Second + Second / 2);
        var cut = tc.View.Preview!;
        Assert.Equal(PreviewPhase.Tail, cut.Phase);
        Assert.Null(cut.Outgoing);
        Assert.Equal(0.5, cut.Incoming!.Value.TotalSeconds, 3);
        Assert.Equal(0.3, tc.Rendered.DcLevel(tc.Rendered.Frames), 3);

        tc.Output.Advance(5 * Second);

        Assert.Null(tc.View.Preview);
        Assert.Equal(3 * Second + 10 * Second, tc.Rendered.Frames);
        Assert.Empty(tc.Notices);
    }

    [Fact]
    public void AnUnauthoredDraft_WithTheDefaultCrossfadeOn_PlaysTheDefaultCrossfade()
    {
        using var tc = TestEngine.Create(new PlayerSettings
        {
            UnauthoredAdjacency = UnauthoredAdjacency.DefaultCrossfade,
            DefaultCrossfadeSeconds = 4,
        });
        var (a, b) = QueuePlayingAt3s(tc);

        tc.Engine.Preview(tc.AdjacencyOf(a, b), new TransitionSpec());
        tc.Output.Advance(7 * Second); // the crossfade starts at 16 s, 5 s in

        var view = tc.View.Preview!;
        Assert.Equal(PreviewPhase.Transition, view.Phase);
        Assert.Equal(18.0, view.Outgoing!.Value.TotalSeconds, 3);
        Assert.Equal(2.0, view.Incoming!.Value.TotalSeconds, 3);
    }

    // ── Failures ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DeviceLoss_DuringAPreview_EndsIt_AndTheHeldQueueStaysPausedWhereItWasHeld()
    {
        using var tc = TestEngine.Create();
        HeldMidPreview(tc);

        tc.Output.Fail(new IOException("The device was unplugged."));

        Assert.Null(tc.View.Preview);
        Assert.Equal(Transport.Paused, tc.View.Transport);
        Assert.Equal(3.0, tc.Playhead.TotalSeconds, 3);
        Assert.Contains(tc.Notices, n => n.Contains("unplugged") && n.Contains("preview ended"));

        tc.Engine.Play();
        tc.Output.Advance(Second);

        Assert.Equal(1, tc.Output.ReopenCount);
        Assert.Equal(4.0, tc.Playhead.TotalSeconds, 3);
        Assert.Equal(0.20, tc.Rendered.DcLevel(tc.Rendered.Frames), 3); // A's fourth second
    }

    [Fact]
    public void AnUnreadableTrack_MeansThePreviewDoesNotStart_TheRowIsMarked_AndTheQueueIsNeverHeld()
    {
        using var tc = TestEngine.Create();
        var (a, b) = QueuePlayingAt3s(tc);
        var adjacency = tc.AdjacencyOf(a, b);
        File.Delete(b);

        tc.Engine.Preview(adjacency, Fade(12, 2, fadeInPoint: 4, fadeIn: 2));
        tc.Output.Advance(Second);

        Assert.Null(tc.View.Preview);
        Assert.Contains(tc.Notices, n => n.Contains("Could not preview") && n.Contains(Path.GetFileName(b)));
        Assert.True(tc.View.Queue.Single(r => r.Track.FilePath == b).IsUnreadable);
        Assert.Equal(Transport.Playing, tc.View.Transport);
        Assert.Equal(4.0, tc.Playhead.TotalSeconds, 3);
        Assert.Empty(tc.Output.EmittedAtStop); // the device was never stopped
    }

    [Fact]
    public void AnOutgoingTrackWhoseAudioEndsEarly_EndsThatVoiceWithANotice_AndThePreviewCarriesOn()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 20, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 20, 0.3);
        tc.Play(a, b);
        tc.Output.Advance(Second);
        var adjacency = tc.AdjacencyOf(a, b);
        tc.Engine.Stop();
        TruncateWav(a, written: 20, seconds: 9); // the header still claims 20 s

        tc.Engine.Preview(adjacency, Fade(12, 2, fadeInPoint: 0, fadeIn: 2));
        tc.Output.Advance(2 * Second + Second / 2); // A's audio ended at 9 s, 2 s in

        var view = tc.View.Preview!;
        Assert.Null(view.Outgoing);
        Assert.Equal(0.5, view.Incoming!.Value.TotalSeconds, 3); // the boundary fired where A ended
        Assert.Contains(tc.Notices, n => n.Contains(Path.GetFileName(a)) && n.Contains("before its header claims"));

        tc.Output.Advance(7 * Second); // ends 2 s of fade-in and 5 s of tail after 2 s in

        Assert.Null(tc.View.Preview);
        Assert.Equal(9 * Second, tc.Rendered.Frames - Second);
    }

    [Fact]
    public void ADecodeError_EndsThatVoiceWithANotice_AndThePreviewCarriesOnToItsEnd()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 6, 0.4);
        var b = UnreadableTracksTests.CorruptFlac(out var failsAt);
        Assert.True(failsAt < 5 * Second, "the damage must fall inside the tail");
        var adjacency = new Adjacency(tc.TrackOf(a), tc.TrackOf(b));

        tc.Engine.Preview(adjacency, new TransitionSpec()); // a hard cut at A's end, 5 s in
        tc.Output.Advance(5 * Second + (int)failsAt + Second / 10);

        Assert.Equal(PreviewPhase.Tail, tc.View.Preview!.Phase);
        Assert.Null(tc.View.Preview.Incoming);
        Assert.Contains(tc.Notices, n => n.Contains(Path.GetFileName(b)) && n.Contains("could not be read past"));

        tc.Output.Advance(10 * Second);

        Assert.Null(tc.View.Preview);
        Assert.Equal(10 * Second, tc.Rendered.Frames);
    }

    [Fact]
    public void ADraftWithAnImpossibleValue_IsRefusedAsSaveRefusesIt_AndNothingIsHeld()
    {
        using var tc = TestEngine.Create();
        var (a, b) = QueuePlayingAt3s(tc);

        var refusals = tc.Engine.Preview(tc.AdjacencyOf(a, b), Fade(25, 2, fadeInPoint: 4, fadeIn: 2));

        Assert.Equal(TransitionField.FadeOutPoint, Assert.Single(refusals).Field);
        Assert.Contains(tc.Notices, n => n.StartsWith("Not previewed"));
        Assert.Null(tc.View.Preview);
        Assert.Equal(Transport.Playing, tc.View.Transport);
    }

    [Fact]
    public void AQueueHeldMidTransition_IsHeldInItsTransition_AndResumesIt()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 6, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 6, 0.2);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, Fade(2, 2, fadeInPoint: 1, fadeIn: 2));
        tc.Engine.Play();
        tc.Output.Advance(3 * Second); // 1 s into the crossfade: B heard at 2 s

        tc.Engine.Preview(tc.AdjacencyOf(a, b), new TransitionSpec());
        tc.Output.Advance(2 * Second);

        var held = tc.View;
        Assert.Equal(Transport.Paused, held.Transport);
        Assert.Equal(a, held.Attended!.Row.Track.FilePath);
        Assert.Equal(3.0, held.Attended.Playhead.TotalSeconds, 3);
        Assert.Equal(b, held.Incoming!.Row.Track.FilePath);
        Assert.Equal(2.0, held.Incoming.Playhead.TotalSeconds, 3);

        tc.Engine.StopPreview();
        tc.Engine.Play();
        tc.Output.Advance(Second / 2);

        // Half a second on in the crossfade: A at 0.4 × 0.25 and B at 0.2 × 0.75.
        Assert.Equal(0.25, tc.Rendered.DcLevel(tc.Rendered.Frames, window: 32), 2);
        Assert.Equal(3.5, tc.Playhead.TotalSeconds, 3);
        Assert.Equal(2.5, tc.View.Incoming!.Playhead.TotalSeconds, 3);
    }

    [Fact]
    public void APlayingQueue_IsHeardUpToWhatItHadRendered_ThenThePreviewPlays()
    {
        using var tc = TestEngine.Create(latency: Second / 2);
        var (a, b) = QueuePlayingAt3s(tc); // rendered to 3 s, heard at 2.5 s

        tc.Engine.Preview(tc.AdjacencyOf(a, b), Fade(12, 2, fadeInPoint: 4, fadeIn: 2));
        Assert.Equal(2.5, tc.Playhead.TotalSeconds, 3);
        Assert.Equal(7.0, tc.View.Preview!.Outgoing!.Value.TotalSeconds, 3); // not heard yet

        tc.Output.Advance(Second);

        Assert.Equal(3.0, tc.Playhead.TotalSeconds, 3); // held where the device had got to
        Assert.Equal(7.5, tc.View.Preview!.Outgoing!.Value.TotalSeconds, 3);
        Assert.Empty(tc.Output.EmittedAtStop); // the device played on

        tc.Engine.StopPreview();
        tc.Engine.Play();
        tc.Output.Advance(Second);

        Assert.Equal(3.5, tc.Playhead.TotalSeconds, 3); // 1 s rendered, 0.5 s of it heard
        Assert.Equal(0.20, tc.Rendered.DcLevel(tc.Rendered.Frames), 3); // A's fourth second
    }

    // ── What it sounds like ──────────────────────────────────────────────────────────────

    [Fact]
    public void APreviewsSamples_MatchARenderOfTheSameDraft()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 12, 440);
        var b = TestAudio.WriteToneWav("b.wav", 12, 660);
        var adjacency = new Adjacency(tc.TrackOf(a), tc.TrackOf(b));
        var draft = new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(6), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(2),
            FadeInPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
            FadeInDuration = TimeSpan.FromSeconds(3),
            Curve = FadeCurveKind.EqualPower,
        };
        var store = new Transitions.JsonTransitionStore(Path.Combine(tc.TempDir, "render-store.json"));
        store.Upsert(a, b, draft);
        var wav = Path.Combine(tc.TempDir, "boundary.wav");
        Renderer.RenderBoundary(adjacency, store, wav);

        tc.Engine.Preview(adjacency, draft);
        tc.Output.Advance(14 * Second);

        var rendered = ReadLeft(wav);
        var previewed = tc.Rendered;
        var frames = Math.Min(rendered.Count, previewed.Frames);
        Assert.True(frames >= 12 * Second, $"only {frames} frames to compare");
        for (var f = 0; f < frames; f++)
        {
            Assert.True(Math.Abs(rendered[f] - previewed.Sample(f, 0)) < 1e-6, $"frame {f} differs");
        }
    }

    [Fact]
    public void APreview_PlaysOnTheEnginesOneOutput_WhichTheHeldQueueGivesUp()
    {
        using var tc = TestEngine.Create();
        var (a, b) = QueuePlayingAt3s(tc);

        tc.Engine.Preview(tc.AdjacencyOf(a, b), Fade(12, 2, fadeInPoint: 4, fadeIn: 2));
        tc.Output.Advance(Second);

        Assert.Equal(Transport.Playing, tc.Output.State);
        Assert.Empty(tc.Output.EmittedAtStop); // it plays on from the queue into the preview
        Assert.Equal(0, tc.Output.ReopenCount);
        Assert.Equal(0.40, tc.Rendered.DcLevel(tc.Rendered.Frames), 3); // the preview, not the queue
    }

    [Fact]
    public void PreviewVoices_PlayAtTheirTracksReplayGain_AndTheToggleReachesThem()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 20, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 20, 0.3);
        var adjacency = new Adjacency(tc.TrackOf(a, replayGainDb: -6), tc.TrackOf(b, replayGainDb: +3));

        tc.Engine.Preview(adjacency, Fade(12, 0, fadeInPoint: 0, fadeIn: 0));
        tc.Output.Advance(Second);
        Assert.Equal(0.4 * ReplayGain.DbToMultiplier(-6), tc.Rendered.DcLevel(tc.Rendered.Frames), 3);

        tc.Engine.ChangeSettings(tc.View.Settings with { ReplayGainEnabled = false });
        tc.Output.Advance(Second);
        Assert.Equal(0.4, tc.Rendered.DcLevel(tc.Rendered.Frames), 3);

        tc.Engine.ChangeSettings(tc.View.Settings with { ReplayGainEnabled = true });
        tc.Output.Advance(5 * Second); // B, from A's end at 20 s
        Assert.Equal(0.3 * ReplayGain.DbToMultiplier(+3), tc.Rendered.DcLevel(tc.Rendered.Frames), 3);
    }

    private static List<float> ReadLeft(string path)
    {
        using var reader = new WaveFileReader(path);
        var left = new List<float>();
        var frame = new float[reader.WaveFormat.Channels];
        var provider = reader.ToSampleProvider();
        while (provider.Read(frame, 0, frame.Length) == frame.Length)
        {
            left.Add(frame[0]);
        }
        return left;
    }

    /// <summary>Cuts a 16-bit stereo 44.1 kHz WAV of <paramref name="written"/> seconds to
    /// <paramref name="seconds"/> without touching its header.</summary>
    private static void TruncateWav(string path, double written, double seconds)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        var headerBytes = stream.Length - (long)(written * Second) * 4;
        stream.SetLength(headerBytes + (long)(seconds * Second) * 4);
    }
}
