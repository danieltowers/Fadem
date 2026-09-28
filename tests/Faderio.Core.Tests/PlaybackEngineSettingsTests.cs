using Faderio.Core.Domain;
using Faderio.Core.Settings;

namespace Faderio.Core.Tests;

/// <summary>Settings-driven behaviour through the seam. Output mode is no longer a setting the
/// engine applies: the output is injected, fixed at launch (ADR 0008).</summary>
public sealed class PlaybackEngineSettingsTests
{
    [Fact]
    public void UnauthoredAdjacency_HardCuts_ByDefault()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 3, 1000);
        var b = TestAudio.WriteToneWav("b.wav", 3, 2000);
        tc.Play(a, b);
        tc.Output.Advance(44100 * 2); // 2.0 s — mid A

        Assert.Equal(0.4, tc.Rendered.ToneAmplitude(1000, 88200), 2);
        Assert.True(tc.Rendered.ToneAmplitude(2000, 88200) < 0.05); // no crossfade unless authored
    }

    [Fact]
    public void UnauthoredAdjacency_WithTheDefaultCrossfadeOn_Crossfades()
    {
        var settings = new PlayerSettings
        {
            UnauthoredAdjacency = UnauthoredAdjacency.DefaultCrossfade,
            DefaultCrossfadeSeconds = 1,
        };
        using var tc = TestEngine.Create(settings);
        var a = TestAudio.WriteToneWav("a.wav", 3, 1000);
        var b = TestAudio.WriteToneWav("b.wav", 3, 2000);
        tc.Play(a, b);
        tc.Output.Advance(44100 * 3);

        // Mid default-crossfade (2.5 s): both tracks audible at equal-power gains.
        var gA = tc.Rendered.ToneAmplitude(1000, 110250) / 0.4;
        var gB = tc.Rendered.ToneAmplitude(2000, 110250) / 0.4;
        Assert.InRange(gA, 0.6, 0.8); // cos(π/4)
        Assert.InRange(gB, 0.6, 0.8); // sin(π/4)
    }

    [Fact]
    public void TheEngineRuns_AtTheOutputsRate()
    {
        // ADR 0008: a 48 kHz device makes a 48 kHz engine. A 48 kHz file then plays without
        // resampling, and a second of it is 48000 rendered frames.
        var dir = Path.Combine(TestTemp.Root, "engine", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var output = new FakeAudioOutput { SampleRate = 48000 };
        var sink = new Faderio.Core.Playback.RecordingSink();
        using var engine = new Faderio.Core.Playback.PlaybackEngine(
            output, new Faderio.Core.Transitions.JsonTransitionStore(Path.Combine(dir, "t.json")), sink);
        var a = TestAudio.WriteWavAt("a48", 2, _ => 0.4, 48000, 16, 2);

        Assert.Equal(48000, engine.SampleRate);
        engine.Add(new[] { a }).Wait();
        engine.Play();
        output.Advance(48000);

        Assert.Equal(1.0, engine.View.Attended!.Playhead.TotalSeconds, 3);
        Assert.Equal(2000, engine.View.Attended!.Length.Milliseconds);
        Assert.Equal(0.4, new RenderedAudio(output.Blocks).DcLevel(48000), 2);
    }
}
