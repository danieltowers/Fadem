using Faderio.Core.Domain;
using Faderio.Core.Playback;

namespace Faderio.Core.Tests;

/// <summary>Transport and queue boundaries through the seam (issues 0002, 0003).</summary>
public sealed class PlaybackEngineTransportTests
{
    [Fact]
    public void SingleTrack_PlaysAndTracksPosition()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        tc.SetQueueOf(a);
        tc.Engine.Play();
        tc.Output.Advance(44100); // one second

        Assert.Equal(PlaybackState.Playing, tc.Engine.State);
        Assert.Equal(1.0, tc.Engine.CurrentPosition.TotalSeconds, 3);
        Assert.Equal(0.4, tc.Rendered.DcLevel(44100), 2);
    }

    [Fact]
    public void Pause_StopsTheClock_ResumeContinues()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        tc.SetQueueOf(a);
        tc.Engine.Play();
        tc.Output.Advance(44100);
        tc.Engine.Pause();
        var pausedFrames = tc.Output.FramesRendered;
        tc.Output.Advance(44100);
        Assert.Equal(pausedFrames, tc.Output.FramesRendered); // a paused device pulls nothing

        tc.Engine.Play();
        tc.Output.Advance(22050);
        Assert.Equal(1.5, tc.Engine.CurrentPosition.TotalSeconds, 3);
    }

    [Fact]
    public void Stop_ResetsPlayback_RestartPlaysFromTheTop()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        tc.SetQueueOf(a);
        tc.Engine.Play();
        tc.Output.Advance(44100);
        tc.Engine.Stop();

        Assert.Equal(PlaybackState.Stopped, tc.Engine.State);
        Assert.Equal(TimeSpan.Zero, tc.Engine.CurrentPosition);

        tc.Engine.Play();
        tc.Output.Advance(4410);
        Assert.Equal(0.1, tc.Engine.CurrentPosition.TotalSeconds, 3);
    }

    [Fact]
    public void Seek_WithinCurrentTrack_MovesTheDecodePosition()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 5, 0.4);
        tc.SetQueueOf(a);
        tc.Engine.Play();
        tc.Output.Advance(4410);
        tc.Engine.Seek(TimeSpan.FromSeconds(2));
        Assert.Equal(2.0, tc.Engine.CurrentPosition.TotalSeconds, 3);
        tc.Output.Advance(44100);
        Assert.Equal(3.0, tc.Engine.CurrentPosition.TotalSeconds, 3);
    }

    [Fact]
    public void Queue_HardCutBoundary_IsSeamlessAndSampleAccurate()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 3, 0.2);
        tc.SetQueueOf(a, b);
        tc.Engine.Play();
        tc.Output.Advance(44100 * 3 + 4410); // 3.1 s

        Assert.Equal(b, tc.Engine.CurrentTrack!.FilePath);
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
        tc.SetQueueOf(a, b);
        tc.Engine.Play();
        tc.Output.Advance(4410);
        tc.Engine.Next();
        Assert.Equal(b, tc.Engine.CurrentTrack!.FilePath);

        tc.Engine.Previous(); // position < 3 s → previous track
        Assert.Equal(a, tc.Engine.CurrentTrack!.FilePath);

        tc.Output.Advance(44100 * 4); // past 3 s
        tc.Engine.Previous(); // position > 3 s → restart current
        Assert.Equal(TimeSpan.Zero, tc.Engine.CurrentPosition);
    }

    [Fact]
    public void PlaybackFinished_FiresExactlyOnce_PerRun()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.2);
        tc.SetQueueOf(a, b);
        tc.Engine.Play();
        tc.Output.Advance(44100 * 3);

        Assert.Equal(1, tc.PlaybackFinishedCount);
        Assert.Equal(PlaybackState.Stopped, tc.Engine.State);

        tc.Output.Advance(44100); // more clock: still once
        Assert.Equal(1, tc.PlaybackFinishedCount);

        tc.Engine.Play();
        tc.Output.Advance(44100 * 3);
        Assert.Equal(2, tc.PlaybackFinishedCount); // exactly once per playback run
    }
}
