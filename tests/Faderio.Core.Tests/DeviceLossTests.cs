using Faderio.Core.Domain;
using Faderio.Core.Playback;

namespace Faderio.Core.Tests;

/// <summary>Device loss, through the seam (build issue 11): the output fails on its own, the engine
/// lands paused at what the listener heard, and never resumes by itself. Play reopens the output;
/// the engine rate never changes (ADR 0008). The staircase track is 0.1 in its first second, 0.2
/// in its second and so on, so the rendered level says where in the track playback resumed.</summary>
public sealed class DeviceLossTests
{
    private const int Second = 44100;

    private static string Staircase(string name, double seconds) =>
        TestAudio.WriteWav(name, seconds, f => 0.1 * (1 + f / Second));

    private static void Unplug(TestEngine tc) => tc.Output.Fail(new IOException("The device was unplugged."));

    private static TransitionSpec Fade(double fadeOutPoint, double fadeOut, double fadeInPoint, double fadeIn) => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(fadeOutPoint), FromEnd: false),
        FadeOutDuration = TimeSpan.FromSeconds(fadeOut),
        FadeInPoint = new FadePointSpec(TimeSpan.FromSeconds(fadeInPoint), FromEnd: false),
        FadeInDuration = TimeSpan.FromSeconds(fadeIn),
        Curve = FadeCurveKind.Linear,
    };

    [Fact]
    public void AFailureMidTrack_PausesAtTheHeardPlayhead_WithANotice()
    {
        using var tc = TestEngine.Create(latency: Second / 2);
        var a = Staircase("a.wav", 6);
        tc.Play(a);
        tc.Output.Advance(3 * Second); // heard: 2.5 s

        Unplug(tc);

        Assert.Equal(Transport.Paused, tc.View.Transport);
        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(2.5, tc.Playhead.TotalSeconds, 2);
        Assert.Contains(tc.Notices, n => n.Contains("unplugged"));
    }

    [Fact]
    public void AfterAFailure_NothingResumesByItself()
    {
        using var tc = TestEngine.Create(latency: Second / 2);
        var a = Staircase("a.wav", 6);
        tc.Play(a);
        tc.Output.Advance(3 * Second);
        Unplug(tc);
        var rendered = tc.Rendered.Frames;

        tc.Output.Advance(Second);

        Assert.Equal(rendered, tc.Rendered.Frames);
        Assert.Equal(Transport.Paused, tc.View.Transport);
        Assert.Equal(0, tc.Output.ReopenCount);
    }

    [Fact]
    public void PlayAfterAFailure_ReopensTheOutput_AndResumesFromTheHeardPlayhead()
    {
        using var tc = TestEngine.Create(latency: Second / 2);
        var a = Staircase("a.wav", 6);
        tc.Play(a);
        tc.Output.Advance(3 * Second); // rendered into 0.4, heard in 0.3
        Unplug(tc);

        tc.Engine.Play();
        tc.Output.Advance(Second);

        Assert.Equal(1, tc.Output.ReopenCount);
        Assert.Equal(Transport.Playing, tc.View.Transport);
        Assert.Equal(0.3, tc.Rendered.DcLevel(3 * Second + Second / 10), 2);
        Assert.Equal(3.0, tc.Playhead.TotalSeconds, 2); // 2.5 s + 1 s rendered − 0.5 s latency
    }

    [Fact]
    public void AFailureMidTransition_LandsPausedOnTheIncomingTrack_AtItsHeardPosition()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 6, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 6, 0.2);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, Fade(2, 2, fadeInPoint: 1, fadeIn: 2));
        tc.Engine.Play();
        tc.Output.Advance(3 * Second); // 1 s into the crossfade: B heard at 2 s

        Unplug(tc);

        Assert.Equal(Transport.Paused, tc.View.Transport);
        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
        Assert.Null(tc.View.Incoming);
        Assert.Equal(2.0, tc.Playhead.TotalSeconds, 2);

        tc.Engine.Play();
        tc.Output.Advance(Second / 2);

        // B alone at full volume: the outgoing voice was released with the transition.
        Assert.Equal(0.2, tc.Rendered.DcLevel(3 * Second + Second / 2), 3);
        Assert.Equal(2.5, tc.Playhead.TotalSeconds, 2);
    }

    [Fact]
    public void AHardCutStillInTheDeviceBuffer_LandsOnTheOutgoingTrack_WhereItWasHeard()
    {
        using var tc = TestEngine.Create(latency: Second / 2);
        var a = Staircase("a.wav", 2);
        var b = Staircase("b.wav", 6);
        tc.Play(a, b);
        tc.Output.Advance(2 * Second + Second / 5); // B rendered from 2 s; A heard at 1.7 s

        Unplug(tc);

        Assert.Equal(Transport.Paused, tc.View.Transport);
        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(1.7, tc.Playhead.TotalSeconds, 2);
    }

    [Fact]
    public void AFailureInTheLastTracksTail_LandsPausedWhereItWasHeard_AndPlayFinishesIt()
    {
        using var tc = TestEngine.Create(latency: Second / 2);
        var a = Staircase("a.wav", 2);
        tc.Play(a);
        tc.Output.Advance(2 * Second); // all of A rendered; heard to 1.5 s

        Unplug(tc);

        Assert.Equal(Transport.Paused, tc.View.Transport);
        Assert.Equal(1.5, tc.Playhead.TotalSeconds, 2);

        tc.Engine.Play();
        tc.Output.Advance(2 * Second);

        Assert.Equal(0.2, tc.Rendered.DcLevel(2 * Second + Second / 10), 2); // the unheard tail
        Assert.Equal(Transport.Stopped, tc.View.Transport);
    }

    [Fact]
    public void AFailureWhilePaused_StaysPausedWhereItWas()
    {
        using var tc = TestEngine.Create();
        var a = Staircase("a.wav", 6);
        tc.Play(a);
        tc.Output.Advance(2 * Second);
        tc.Engine.Pause();

        Unplug(tc);

        Assert.Equal(Transport.Paused, tc.View.Transport);
        Assert.Equal(2.0, tc.Playhead.TotalSeconds, 2);
    }

    [Fact]
    public void AReopenAtADifferentRate_SuggestsARestart_AndTheEngineRateIsUnchanged()
    {
        using var tc = TestEngine.Create();
        var a = Staircase("a.wav", 6);
        tc.Play(a);
        tc.Output.Advance(Second);
        Unplug(tc);
        tc.Output.DeviceRate = 48000;

        tc.Engine.Play();

        Assert.Equal(Transport.Playing, tc.View.Transport);
        Assert.Equal(44100, tc.Engine.SampleRate);
        Assert.Contains(tc.Notices, n => n.Contains("48000") && n.Contains("restart", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AReopenAtTheSameRate_SaysNothingAboutRates()
    {
        using var tc = TestEngine.Create();
        var a = Staircase("a.wav", 6);
        tc.Play(a);
        tc.Output.Advance(Second);
        Unplug(tc);
        var before = tc.Notices.Count;

        tc.Engine.Play();

        Assert.Equal(before, tc.Notices.Count);
    }

    [Fact]
    public void WithNoDevice_PlayStaysPaused_WithANotice_AndTriesAgainNextTime()
    {
        using var tc = TestEngine.Create();
        var a = Staircase("a.wav", 6);
        tc.Play(a);
        tc.Output.Advance(2 * Second);
        Unplug(tc);
        tc.Output.NoDevice = true;
        var before = tc.Notices.Count;

        tc.Engine.Play();

        Assert.Equal(Transport.Paused, tc.View.Transport);
        Assert.Equal(2.0, tc.Playhead.TotalSeconds, 2);
        Assert.True(tc.Notices.Count > before);

        tc.Output.NoDevice = false;
        tc.Engine.Play();
        tc.Output.Advance(Second);

        Assert.Equal(Transport.Playing, tc.View.Transport);
        Assert.Equal(3.0, tc.Playhead.TotalSeconds, 2);
    }

    [Fact]
    public void PlayingARowAfterAFailure_ReopensTheOutput()
    {
        using var tc = TestEngine.Create();
        var a = Staircase("a.wav", 6);
        var b = Staircase("b.wav", 6);
        tc.Play(a, b);
        tc.Output.Advance(Second);
        Unplug(tc);

        tc.Engine.PlayRow(tc.RowOf(b));
        tc.Output.Advance(Second);

        Assert.Equal(1, tc.Output.ReopenCount);
        Assert.Equal(Transport.Playing, tc.View.Transport);
        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(1.0, tc.Playhead.TotalSeconds, 2);
    }

    [Fact]
    public void StopAfterAFailure_Stops_AndTheNextPlayReopensFromTheTop()
    {
        using var tc = TestEngine.Create();
        var a = Staircase("a.wav", 6);
        tc.Play(a);
        tc.Output.Advance(2 * Second);
        Unplug(tc);

        tc.Engine.Stop();
        Assert.Equal(Transport.Stopped, tc.View.Transport);

        tc.Engine.Play();
        tc.Output.Advance(Second);

        Assert.Equal(1, tc.Output.ReopenCount);
        Assert.Equal(Transport.Playing, tc.View.Transport);
        Assert.Equal(1.0, tc.Playhead.TotalSeconds, 2);
    }

    [Fact]
    public void SkippingAfterAFailure_LandsPausedOnTheNextTrack()
    {
        using var tc = TestEngine.Create();
        var a = Staircase("a.wav", 6);
        var b = Staircase("b.wav", 6);
        tc.Play(a, b);
        tc.Output.Advance(2 * Second);
        Unplug(tc);

        tc.Engine.Next();

        Assert.Equal(Transport.Paused, tc.View.Transport);
        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(0.0, tc.Playhead.TotalSeconds, 2);
    }

    [Fact]
    public void AFailureWhileStopped_LeavesPlaybackStopped_AndPlayReopens()
    {
        using var tc = TestEngine.Create();
        var a = Staircase("a.wav", 6);
        tc.Add(a);

        Unplug(tc);
        Assert.Equal(Transport.Stopped, tc.View.Transport);

        tc.Engine.Play();
        Assert.Equal(1, tc.Output.ReopenCount);
        Assert.Equal(Transport.Playing, tc.View.Transport);
    }
}
