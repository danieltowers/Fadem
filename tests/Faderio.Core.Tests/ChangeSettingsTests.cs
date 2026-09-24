using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Settings;
using Faderio.Core.Transitions;

namespace Faderio.Core.Tests;

/// <summary>
/// Build issue 12: the engine owns settings.json. <c>ChangeSettings</c> saves the file and
/// applies each value: a changed default crossfade reschedules a boundary whose transition has
/// not started and never touches one in flight; an output mode waits for the next launch.
/// </summary>
public sealed class ChangeSettingsTests
{
    private const int Rate = 44100;

    private static PlayerSettings Crossfade(int seconds) =>
        new() { UnauthoredAdjacency = UnauthoredAdjacency.DefaultCrossfade, DefaultCrossfadeSeconds = seconds };

    private static int At(double seconds) => (int)(seconds * Rate);

    [Fact]
    public void ChangeSettings_SavesTheFile_AndTheViewCarriesThem()
    {
        using var tc = TestEngine.Create();
        var chosen = Crossfade(8) with { OutputMode = AudioOutputMode.WaveOut, ReplayGainEnabled = false };

        tc.Engine.ChangeSettings(chosen);

        Assert.Equal(chosen, tc.View.Settings);
        Assert.Equal(chosen, tc.Sink.Latest!.Settings);
        Assert.Equal(chosen, new SettingsStore(tc.SettingsPath).Settings); // survives a restart
        Assert.Equal("Saved settings.", Assert.Single(tc.Notices));
    }

    [Fact]
    public void TheViewCarries_TheSettingsLoadedAtLaunch()
    {
        using var tc = TestEngine.Create(Crossfade(12));
        Assert.Equal(Crossfade(12), tc.View.Settings);
    }

    [Fact]
    public void AnOutOfRangeN_IsBroughtIntoRange()
    {
        using var tc = TestEngine.Create();
        tc.Engine.ChangeSettings(Crossfade(45));
        Assert.Equal(30, tc.View.Settings.DefaultCrossfadeSeconds);
    }

    [Fact]
    public void ANewOutputMode_IsSavedForTheNextLaunch_AndTheEngineRateIsUnchanged()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 3, 1000);
        tc.Play(a);
        tc.Output.Advance(Rate);

        tc.Engine.ChangeSettings(new PlayerSettings { OutputMode = AudioOutputMode.WasapiExclusive });

        Assert.Equal(AudioOutputMode.WasapiExclusive, new SettingsStore(tc.SettingsPath).Settings.OutputMode);
        Assert.Equal(Rate, tc.Engine.SampleRate);
        Assert.Equal(Transport.Playing, tc.View.Transport);
        Assert.Equal(1.0, tc.Playhead.TotalSeconds, 3);
    }

    [Fact]
    public void ACorruptSettingsFile_IsANotice_AndDefaultsLoad()
    {
        var dir = Path.Combine(Path.GetTempPath(), "faderio-engine", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, "{ not json");
        var sink = new RecordingSink();

        using var engine = new PlaybackEngine(
            new FakeAudioOutput(), new JsonTransitionStore(Path.Combine(dir, "t.json")), sink, new SettingsStore(path));

        Assert.Equal(new PlayerSettings(), engine.View.Settings);
        var notice = Assert.Single(sink.Notices);
        Assert.Contains("moved aside to settings.json.corrupt", notice);
    }

    [Fact]
    public void ASettingsFileThatCannotBeWritten_ChangesNothing_AndSaysWhy()
    {
        using var tc = TestEngine.Create();
        Directory.CreateDirectory(tc.SettingsPath); // a folder where the file should be: every write fails
        var a = TestAudio.WriteToneWav("a.wav", 3, 1000);
        var b = TestAudio.WriteToneWav("b.wav", 3, 2000);
        tc.Play(a, b);

        tc.Engine.ChangeSettings(Crossfade(1));

        Assert.Equal(new PlayerSettings(), tc.View.Settings);
        Assert.Contains(tc.Notices, n => n.StartsWith("Not saved: settings. The settings file could not be written", StringComparison.Ordinal));
        tc.Output.Advance(At(3));
        Assert.True(tc.Rendered.ToneAmplitude(2000, At(2.5)) < 0.05); // still the hard cut
    }

    [Fact]
    public void SwitchingToTheDefaultCrossfade_MidTrack_MakesTheNextUnauthoredAdjacencyCrossfade()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 3, 1000);
        var b = TestAudio.WriteToneWav("b.wav", 3, 2000);
        tc.Play(a, b);
        tc.Output.Advance(At(1));

        tc.Engine.ChangeSettings(Crossfade(1));
        tc.Output.Advance(At(2));

        // Mid default crossfade (2.5 s): both tracks audible at equal-power gains.
        Assert.InRange(tc.Rendered.ToneAmplitude(1000, At(2.5)) / 0.4, 0.6, 0.8);
        Assert.InRange(tc.Rendered.ToneAmplitude(2000, At(2.5)) / 0.4, 0.6, 0.8);
    }

    [Fact]
    public void ChangingN_ReschedulesThePendingDefaultCrossfade()
    {
        using var tc = TestEngine.Create(Crossfade(1));
        var a = TestAudio.WriteToneWav("a.wav", 4, 1000);
        var b = TestAudio.WriteToneWav("b.wav", 4, 2000);
        tc.Play(a, b);
        tc.Output.Advance(At(0.5));

        tc.Engine.ChangeSettings(Crossfade(2));
        tc.Output.Advance(At(3));

        // Two seconds from 2.0 s: at 3.0 s it is half way. One second would only be starting.
        Assert.InRange(tc.Rendered.ToneAmplitude(1000, At(3)) / 0.4, 0.6, 0.8);
        Assert.InRange(tc.Rendered.ToneAmplitude(2000, At(3)) / 0.4, 0.6, 0.8);
    }

    [Fact]
    public void SwitchingToTheDefaultCrossfade_InATracksLastNSeconds_LeavesItsBoundaryAHardCut()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 3, 1000);
        var b = TestAudio.WriteToneWav("b.wav", 3, 2000);
        tc.Play(a, b);
        tc.Output.Advance(At(2.5)); // a 2 s fade would have begun at 1.0 s

        tc.Engine.ChangeSettings(Crossfade(2));
        tc.Output.Advance(At(1));

        Assert.Equal(0.4, tc.Rendered.ToneAmplitude(1000, At(2.9)), 2);
        Assert.True(tc.Rendered.ToneAmplitude(2000, At(2.9)) < 0.05);
        Assert.Equal(0.4, tc.Rendered.ToneAmplitude(2000, At(3.5)), 2);
    }

    [Fact]
    public void ADefaultCrossfadeInFlight_PlaysOut_AndTheNextBoundaryTakesTheNewSetting()
    {
        using var tc = TestEngine.Create(Crossfade(1));
        var a = TestAudio.WriteToneWav("a.wav", 3, 1000);
        var b = TestAudio.WriteToneWav("b.wav", 3, 2000);
        var c = TestAudio.WriteToneWav("c.wav", 3, 3000);
        tc.Play(a, b, c);
        tc.Output.Advance(At(2.25)); // a → b fades from 2.0 s to 3.0 s

        tc.Engine.ChangeSettings(new PlayerSettings());
        tc.Output.Advance(At(3.75));

        Assert.InRange(tc.Rendered.ToneAmplitude(1000, At(2.5)) / 0.4, 0.6, 0.8); // still fading
        Assert.InRange(tc.Rendered.ToneAmplitude(2000, At(2.5)) / 0.4, 0.6, 0.8);
        // b began at 2.0 s. Under the old setting b → c would fade from 4.0 s; now it is a hard
        // cut at 5.0 s.
        Assert.Equal(0.4, tc.Rendered.ToneAmplitude(2000, At(4.75)), 2);
        Assert.True(tc.Rendered.ToneAmplitude(3000, At(4.75)) < 0.05);
        Assert.Equal(0.4, tc.Rendered.ToneAmplitude(3000, At(5.5)), 2);
    }

    [Fact]
    public void ChangingN_DoesNotAlterAnAuthoredTransition_WithBlankDurations()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 8, 1000);
        var b = TestAudio.WriteToneWav("b.wav", 8, 2000);
        tc.Add(a, b);
        tc.Engine.Author(tc.AdjacencyOf(a, b), new TransitionSpec { FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false) });
        tc.Engine.Play();

        tc.Engine.ChangeSettings(Crossfade(1));
        tc.Output.Advance(At(4));

        // The built-in 5 s from 1.0 s: half way at 3.5 s. N = 1 would have ended it at 2.0 s.
        Assert.InRange(tc.Rendered.ToneAmplitude(1000, At(3.5)) / 0.4, 0.6, 0.8);
        Assert.InRange(tc.Rendered.ToneAmplitude(2000, At(3.5)) / 0.4, 0.6, 0.8);
    }
}
