using Faderio.Core.Domain;
using Faderio.Core.Settings;

namespace Faderio.Core.Tests;

/// <summary>Settings-driven behaviour and observable engine state (issues 0013, 0011, 0008 shuffle).</summary>
public sealed class PlaybackEngineSettingsTests
{
    [Fact]
    public void UnauthoredAdjacency_HardCuts_ByDefault()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 3, 1000);
        var b = TestAudio.WriteToneWav("b.wav", 3, 2000);
        tc.SetQueueOf(a, b);
        tc.Engine.Play();
        tc.Output.Advance(44100 * 2); // 2.0 s — mid A

        Assert.Equal(0.4, tc.Rendered.ToneAmplitude(1000, 88200), 2);
        Assert.True(tc.Rendered.ToneAmplitude(2000, 88200) < 0.05); // no crossfade unless authored
    }

    [Fact]
    public void UnauthoredAdjacency_WithGlobalDefaultCrossfadeSetting_Crossfades()
    {
        var settings = new PlayerSettings
        {
            DefaultAdjacencyBehaviour = DefaultAdjacencyBehaviour.GlobalDefaultCrossfade,
            DefaultCrossfadeDuration = TimeSpan.FromSeconds(1),
        };
        using var tc = TestEngine.Create(settings);
        var a = TestAudio.WriteToneWav("a.wav", 3, 1000);
        var b = TestAudio.WriteToneWav("b.wav", 3, 2000);
        tc.SetQueueOf(a, b);
        tc.Engine.Play();
        tc.Output.Advance(44100 * 3);

        // Mid default-crossfade (2.5 s): both tracks audible at equal-power gains.
        var gA = tc.Rendered.ToneAmplitude(1000, 110250) / 0.4;
        var gB = tc.Rendered.ToneAmplitude(2000, 110250) / 0.4;
        Assert.InRange(gA, 0.6, 0.8); // cos(π/4)
        Assert.InRange(gB, 0.6, 0.8); // sin(π/4)
    }

    [Fact]
    public void OutputModeSetting_RebindsTheAudioOutput()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        tc.SetQueueOf(a);

        tc.ApplySettings(new PlayerSettings { OutputMode = AudioOutputMode.WasapiExclusive });
        Assert.Equal(AudioOutputMode.WasapiExclusive, tc.Output.RequestedMode);

        tc.Engine.Play();
        tc.Output.Advance(4410);
        Assert.Equal(0.4, tc.Rendered.DcLevel(4410), 2);
    }

    [Fact]
    public void VisualiserState_ComesFromTheEngine_NotTheDevice()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        tc.SetQueueOf(a);
        tc.Engine.Play();
        tc.Output.Advance(44100);

        var state = tc.Engine.LatestVisualiserState;
        Assert.InRange(state.Peak, 0.35, 0.45);
        Assert.False(state.Clipping);
        var voice = Assert.Single(state.Voices);
        Assert.InRange(voice.Level, 0.9, 1.01);
        Assert.Equal(0, voice.TrackIndex);
    }

    [Fact]
    public void Shuffle_PlaysThroughAKnownPermutation_AndFiresTheAuthoredPair()
    {
        const int seed = 20260919;
        using var tc = TestEngine.Create(rngSeed: seed);
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 3, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 3, 0.3);

        // Derive the deterministic shuffle the engine's queue will produce.
        var expected = new Faderio.Core.Queue.PlaybackQueue(new Random(seed));
        expected.SetTracks(new[] { new Track { FilePath = a }, new Track { FilePath = b }, new Track { FilePath = c } });
        expected.ShuffleUpcoming();
        var order = expected.Tracks.Select(t => t.FilePath).ToList();

        // Author a transition for whatever pair the shuffle makes adjacent first.
        tc.Store.Upsert(order[0], order[1], new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(0.5),
            FadeInPoint = TimeSpan.Zero,
            FadeInDuration = TimeSpan.FromSeconds(0.5),
        });

        tc.SetQueueOf(a, b, c);
        tc.Engine.Shuffle = true;
        tc.Engine.Play();
        tc.Output.Advance(44100 * 2 + 22050); // 2.5 s: the authored handoff has fired

        Assert.Equal(order[1], tc.Engine.CurrentTrack!.FilePath);
        // The fade fired mid-transition even though the order came from a shuffle:
        // at 1.25 s the outgoing track is neither flat (its level) nor silent.
        var midFadeFrame = (long)(1.25 * 44100);
        var outgoingLevel = tc.Rendered.DcLevel(midFadeFrame);
        Assert.NotEqual(0.4, outgoingLevel, 1);
        Assert.NotEqual(0.0, outgoingLevel, 2);
    }
}
