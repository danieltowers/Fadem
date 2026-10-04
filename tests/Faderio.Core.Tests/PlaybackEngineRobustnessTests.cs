using Faderio.Core.Domain;
using Faderio.Core.Playback;

namespace Faderio.Core.Tests;

/// <summary>Robust file handling through the seam (ADR 0007, ADR 0010) and ReplayGain.</summary>
public sealed class PlaybackEngineRobustnessTests
{
    [Fact]
    public void MissingFile_IsRejectedAtAddTime_WithTheReason()
    {
        using var tc = TestEngine.Create();
        var b = TestAudio.WriteDcWav("b.wav", 2, 0.2);
        var report = tc.Engine.Add(new[] { @"C:\definitely\missing.wav", b }).Result;

        var rejection = Assert.Single(report.Rejected);
        Assert.Equal(RejectedProperty.File, rejection.Property);
        Assert.Contains("missing.wav", rejection.Path);
        Assert.Single(report.Added);
        Assert.Single(tc.View.Queue); // a queued track is a track that will play
    }

    [Fact]
    public void CorruptFile_IsRejectedAtAddTime()
    {
        using var tc = TestEngine.Create();
        var corrupt = TestAudio.WriteWav("corrupt.wav", 1, _ => 0.4);
        File.WriteAllText(corrupt, "this is not a wave file");
        var report = tc.Engine.Add(new[] { corrupt }).Result;

        var rejection = Assert.Single(report.Rejected);
        Assert.Equal(RejectedProperty.File, rejection.Property);
        Assert.Empty(tc.View.Queue);
    }

    [Fact]
    public void FileThatFailsAfterPassingTheProbe_IsPassedOver_WithOneNotice()
    {
        // The probe passed; the file vanished before play. Play-time failures are one notice
        // and the row is passed over, never a notice per block (ADR 0007, build issue 10).
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 2, 0.2);
        tc.Add(a, b);
        File.Delete(a);
        tc.Engine.Play();
        tc.Output.Advance(4410);

        Assert.Equal(b, tc.CurrentTrack!.FilePath);
        Assert.Single(tc.Notices, n => n.Contains(Path.GetFileName(a)));
    }

    [Fact]
    public void AllFilesBroken_LeavesNothingToPlay()
    {
        using var tc = TestEngine.Create();
        var report = tc.Engine.Add(new[] { @"C:\missing-one.wav", @"C:\missing-two.wav" }).Result;
        tc.Engine.Play();

        Assert.Equal(2, report.Rejected.Count);
        Assert.Empty(report.Added);
        Assert.Equal(Transport.Stopped, tc.View.Transport);
    }

    [Fact]
    public void ReplayGain_TrackGainApplies_WithSymmetricClamp()
    {
        using var tc = TestEngine.Create();
        var loud = TestAudio.WriteDcWav("loud.wav", 2, 0.4);
        var quiet = TestAudio.WriteDcWav("quiet.wav", 2, 0.4);
        tc.Engine.AddTracks(new List<Track>
        {
            tc.TrackOf(quiet, replayGainDb: -6.0),
            tc.TrackOf(loud, replayGainDb: +24.0), // clamps to +12 dB
        });
        tc.Engine.Play();
        tc.Output.Advance(44100);
        Assert.Equal(0.4 * ReplayGain.DbToMultiplier(-6), tc.Rendered.DcLevel(22050), 2);

        tc.Engine.Next();
        tc.Output.Advance(44100);
        Assert.Equal(0.4 * ReplayGain.DbToMultiplier(12), tc.Rendered.DcLevel(88200), 1);
    }

    [Fact]
    public void ReplayGainDisabled_PlaysAtUnityGain()
    {
        var settings = new Faderio.Core.Settings.PlayerSettings { ReplayGainMode = Faderio.Core.Settings.ReplayGainMode.Off };
        using var tc = TestEngine.Create(settings);
        var a = TestAudio.WriteDcWav("a.wav", 2, 0.4);
        tc.Engine.AddTracks(new List<Track> { tc.TrackOf(a, replayGainDb: -6.0) });
        tc.Engine.Play();
        tc.Output.Advance(44100);
        Assert.Equal(0.4, tc.Rendered.DcLevel(22050), 2);
    }
}
