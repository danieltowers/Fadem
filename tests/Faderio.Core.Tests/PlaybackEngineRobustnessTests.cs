using Faderio.Core.Domain;
using Faderio.Core.Playback;

namespace Faderio.Core.Tests;

/// <summary>Robust file handling through the seam (issue 0014) and ReplayGain (issue 0012).</summary>
public sealed class PlaybackEngineRobustnessTests
{
    [Fact]
    public void MissingFile_IsSkipped_WithANonBlockingNotice()
    {
        using var tc = TestEngine.Create();
        var b = TestAudio.WriteDcWav("b.wav", 2, 0.2);
        tc.SetQueueOf(@"C:\definitely\missing.wav", b);
        tc.Engine.Play();
        tc.Output.Advance(4410);

        Assert.Equal(b, tc.Engine.CurrentTrack!.FilePath);
        Assert.Contains(tc.Notices, n => n.Contains("missing.wav"));
    }

    [Fact]
    public void CorruptFile_IsSkipped_AndPlaybackContinues()
    {
        using var tc = TestEngine.Create();
        var corrupt = TestAudio.WriteWav("corrupt.wav", 1, _ => 0.4);
        File.WriteAllText(corrupt, "this is not a wave file");
        var b = TestAudio.WriteDcWav("b.wav", 2, 0.2);
        tc.SetQueueOf(corrupt, b);
        tc.Engine.Play();
        tc.Output.Advance(4410);

        Assert.Equal(b, tc.Engine.CurrentTrack!.FilePath);
        Assert.NotEmpty(tc.Notices);
    }

    [Fact]
    public void AllFilesBroken_FinishesPlayback_WithNotices()
    {
        using var tc = TestEngine.Create();
        tc.SetQueueOf(@"C:\missing-one.wav", @"C:\missing-two.wav");
        tc.Engine.Play();

        Assert.Equal(1, tc.PlaybackFinishedCount);
        Assert.Equal(2, tc.Notices.Count);
    }

    [Fact]
    public void ReplayGain_TrackGainApplies_WithSymmetricClamp()
    {
        using var tc = TestEngine.Create();
        var loud = TestAudio.WriteDcWav("loud.wav", 2, 0.4);
        var quiet = TestAudio.WriteDcWav("quiet.wav", 2, 0.4);
        tc.Engine.SetQueue(new List<Track>
        {
            new() { FilePath = quiet, ReplayGainDb = -6.0 },
            new() { FilePath = loud, ReplayGainDb = +24.0 }, // clamps to +12 dB → clipping
        });
        tc.Engine.Play();
        tc.Output.Advance(44100);
        Assert.Equal(0.4 * ReplayGain.DbToMultiplier(-6), tc.Rendered.DcLevel(22050), 2);

        tc.Engine.Next();
        tc.Output.Advance(44100);
        Assert.Equal(0.4 * ReplayGain.DbToMultiplier(12), tc.Rendered.DcLevel(88200), 1);
        Assert.True(tc.Engine.LatestVisualiserState.Clipping);
    }

    [Fact]
    public void ReplayGainDisabled_PlaysAtUnityGain()
    {
        var settings = new Faderio.Core.Settings.PlayerSettings { ReplayGainEnabled = false };
        using var tc = TestEngine.Create(settings);
        var a = TestAudio.WriteDcWav("a.wav", 2, 0.4);
        tc.Engine.SetQueue(new List<Track> { new() { FilePath = a, ReplayGainDb = -6.0 } });
        tc.Engine.Play();
        tc.Output.Advance(44100);
        Assert.Equal(0.4, tc.Rendered.DcLevel(22050), 2);
    }
}
