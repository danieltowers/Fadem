using Fadem.Core.Domain;
using Fadem.Core.Playback;

namespace Fadem.Core.Tests;

/// <summary>Transport and queue boundaries through the seam (ADR 0003): every observation is a
/// View read or a recorded publication; every position is the playhead.</summary>
public sealed class PlaybackEngineTransportTests
{
    [Fact]
    public void SingleTrack_PlaysAndTracksThePlayhead()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        tc.Play(a);
        tc.Output.Advance(44100); // one second

        Assert.Equal(Transport.Playing, tc.View.Transport);
        Assert.Equal(1.0, tc.Playhead.TotalSeconds, 3);
        Assert.Equal(0.4, tc.Rendered.DcLevel(44100), 2);
        Assert.Equal(3000, tc.View.Attended!.Length.Milliseconds);
        Assert.Equal(2000, tc.View.Attended!.Remaining.Milliseconds);
    }

    [Fact]
    public void Pause_StopsTheClock_ResumeContinues()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        tc.Play(a);
        tc.Output.Advance(44100);
        tc.Engine.Pause();
        var pausedFrames = tc.Output.FramesRendered;
        tc.Output.Advance(44100);
        Assert.Equal(pausedFrames, tc.Output.FramesRendered); // a paused device pulls nothing
        Assert.Equal(Transport.Paused, tc.View.Transport);

        tc.Engine.Play();
        tc.Output.Advance(22050);
        Assert.Equal(1.5, tc.Playhead.TotalSeconds, 3);
    }

    [Fact]
    public void Stop_ResetsPlayback_RestartPlaysFromTheTop()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        tc.Play(a);
        tc.Output.Advance(44100);
        tc.Engine.Stop();

        Assert.Equal(Transport.Stopped, tc.View.Transport);
        Assert.Null(tc.View.Attended);
        Assert.Equal(Frames.Zero, tc.Output.FramesEmitted); // both clocks back to one origin

        tc.Engine.Play();
        tc.Output.Advance(4410);
        Assert.Equal(0.1, tc.Playhead.TotalSeconds, 3);
    }

    [Fact]
    public void Seek_WithinTheAttendedTrack_MovesThePlayhead()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 5, 0.4);
        tc.Play(a);
        tc.Output.Advance(4410);
        tc.Engine.Seek(Playhead.FromMilliseconds(2000));
        Assert.Equal(2.0, tc.Playhead.TotalSeconds, 3);
        tc.Output.Advance(44100);
        Assert.Equal(3.0, tc.Playhead.TotalSeconds, 3);
    }

    [Fact]
    public void Queue_HardCutBoundary_IsSeamlessAndSampleAccurate()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 3, 0.2);
        tc.Play(a, b);
        tc.Output.Advance(44100 * 3 + 4410); // 3.1 s

        Assert.Empty(tc.Notices);
        Assert.Equal(b, tc.CurrentTrack!.FilePath);
        Assert.True(tc.View.Queue[1].IsCurrent);
        // The last frame of A and the first frame of B land exactly at 3.000 s.
        Assert.Equal(0.4, tc.Rendered.Sample(132299, 0), 2);
        Assert.Equal(0.2, tc.Rendered.Sample(132300, 0), 2);
    }

    [Fact]
    public void NextAndPrevious_MoveThroughTheQueue()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 3, 0.2);
        tc.Play(a, b);
        tc.Output.Advance(4410);
        tc.Engine.Next();
        Assert.Equal(b, tc.CurrentTrack!.FilePath);

        tc.Engine.Previous(); // playhead < 3 s → previous track
        Assert.Equal(a, tc.CurrentTrack!.FilePath);

        tc.Output.Advance(44100 * 4); // past 3 s
        tc.Engine.Previous(); // playhead > 3 s → restart current
        Assert.Equal(Playhead.Zero, tc.Playhead);
    }

    [Fact]
    public void PlayRow_StartsThatRow_AndAStaleRowIsANotice()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 3, 0.2);
        tc.Add(a, b);

        tc.Engine.PlayRow(tc.RowOf(b));
        tc.Output.Advance(4410);
        Assert.Equal(Transport.Playing, tc.View.Transport);
        Assert.Equal(b, tc.CurrentTrack!.FilePath);
        Assert.Equal(0.2, tc.Rendered.DcLevel(4410), 2);

        tc.Engine.PlayRow(new RowId(999));
        Assert.Equal(b, tc.CurrentTrack!.FilePath);
        Assert.Contains(tc.Notices, n => n.Contains("no longer in the queue"));
    }

    [Fact]
    public void PlaybackFinishes_ExactlyOnce_PerRun()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.2);
        tc.Play(a, b);
        tc.Output.Advance(44100 * 3);

        Assert.Equal(1, tc.PlaybackFinishedCount);
        Assert.Equal(Transport.Stopped, tc.View.Transport);

        tc.Output.Advance(44100); // more clock: still once
        Assert.Equal(1, tc.PlaybackFinishedCount);

        tc.Engine.Play();
        tc.Output.Advance(44100 * 3);
        Assert.Equal(2, tc.PlaybackFinishedCount); // exactly once per playback run
    }

    [Fact]
    public void TheView_IsAValue_NoLiveCollectionCrossesTheSeam()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.2);
        tc.Add(a);
        var before = tc.View;
        tc.Add(b);

        Assert.Single(before.Queue);
        Assert.Equal(2, tc.View.Queue.Count);
        Assert.NotEqual(before.QueueVersion, tc.View.QueueVersion);
    }
}
