using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Settings;

namespace Faderio.Core.Tests;

/// <summary>Transition preview through the seam (issue 0010).</summary>
public sealed class PreviewTests
{
    private const double FA = 1000;
    private const double FB = 2000;

    private static TransitionSpec CrossfadeAt1s() => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
        FadeInPoint = TimeSpan.Zero,
        FadeOutDuration = TimeSpan.FromSeconds(0.5),
        FadeInDuration = TimeSpan.FromSeconds(0.5),
    };

    [Fact]
    public void Preview_PlaysTheTransitionEndToEnd_ThenStopsAutomatically()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 4, FA);
        var b = TestAudio.WriteToneWav("b.wav", 4, FB);
        tc.SetQueueOf(a, b);
        tc.Store.Upsert(a, b, CrossfadeAt1s());

        // Main playback is running…
        tc.Engine.Play();
        tc.Output.Advance(22050); // 0.5 s

        var preview = tc.Engine.BeginPreview(new Adjacency(tc.Track(a), tc.Track(b)));
        Assert.NotNull(preview);
        var previewOutput = tc.Output; // the newly created output is the preview's

        bool ended = false;
        preview.Ended += (_, _) => ended = true;

        // Pre-roll (5 s) → transition → 5 s past the fade-in point → automatic stop.
        previewOutput.Advance(44100 * 8);
        Assert.True(ended);
        Assert.Equal(PlaybackState.Stopped, previewOutput.State);
    }

    [Fact]
    public void Preview_DoesNotDisturbTheMainPlayback()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 4, FA);
        var b = TestAudio.WriteToneWav("b.wav", 4, FB);
        tc.SetQueueOf(a, b);
        tc.Store.Upsert(a, b, CrossfadeAt1s());

        tc.Engine.Play();
        tc.Output.Advance(22050); // 0.5 s
        var mainOutput = tc.Output;
        var mainRendered = mainOutput.FramesRendered;

        var preview = tc.Engine.BeginPreview(new Adjacency(tc.Track(a), tc.Track(b)));
        Assert.NotNull(preview);
        tc.Output.Advance(44100 * 7); // drive the preview output

        // The main output state and position are untouched.
        Assert.Equal(PlaybackState.Playing, mainOutput.State);
        Assert.Equal(0.5, tc.Engine.CurrentPosition.TotalSeconds, 3);
        Assert.Equal(0.4, tc.Rendered.ToneAmplitude(FA, mainRendered), 2); // main audio intact
    }

    [Fact]
    public void Preview_WithPartialAuthoring_PlaysTheEffectiveResult()
    {
        var settings = new PlayerSettings { DefaultCrossfadeDuration = TimeSpan.FromSeconds(1) };
        using var tc = TestEngine.Create(settings);
        var a = TestAudio.WriteToneWav("a.wav", 4, FA);
        var b = TestAudio.WriteToneWav("b.wav", 4, FB);
        tc.SetQueueOf(a, b);
        // Only the fade-in point authored: everything else falls back to defaults (1 s here).
        tc.Store.Upsert(a, b, new TransitionSpec
        {
            FadeInPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
        });

        var preview = tc.Engine.BeginPreview(new Adjacency(tc.Track(a), tc.Track(b)));
        Assert.NotNull(preview);
        tc.Output.Advance(44100 * 10);

        // The effective default crossfade (3.0 s → 4.0 s) was heard: at 3.5 s both tones
        // are present — B playing from its authored 1.0 s fade-in point.
        var rendered = new RenderedAudio(tc.Output.Blocks);
        Assert.True(rendered.ToneAmplitude(FA, 154350) > 0.15);
        Assert.True(rendered.ToneAmplitude(FB, 154350) > 0.15);
    }

    [Fact]
    public void UnauthoredAdjacency_PreviewsAsThePlayerDefault()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 2, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        tc.SetQueueOf(a, b);

        var preview = tc.Engine.BeginPreview(new Adjacency(tc.Track(a), tc.Track(b)));
        Assert.NotNull(preview);
        tc.Output.Advance(44100 * 8);

        // Hard cut: A plays flat (pre-roll), then B starts at full level.
        var rendered = new RenderedAudio(tc.Output.Blocks);
        Assert.Equal(0.4, rendered.DcLevel(44100), 2);
        Assert.Equal(0.2, rendered.DcLevel(44100 * 3), 2);
    }

    [Fact]
    public void PreviewPreRoll_SettingIsHonoured()
    {
        var settings = new PlayerSettings { PreviewPreRollSeconds = 2 };
        using var tc = TestEngine.Create(settings);
        var a = TestAudio.WriteToneWav("a.wav", 4, FA);
        var b = TestAudio.WriteToneWav("b.wav", 4, FB);
        tc.SetQueueOf(a, b);
        tc.Store.Upsert(a, b, CrossfadeAt1s());

        var preview = tc.Engine.BeginPreview(new Adjacency(tc.Track(a), tc.Track(b)));
        Assert.NotNull(preview);
        // With 2 s pre-roll, the fade (authored at 1.0 s) begins 1.0 s into the preview.
        tc.Output.Advance(44100 + 4410); // past the trigger
        var rendered = new RenderedAudio(tc.Output.Blocks);
        Assert.True(rendered.ToneAmplitude(FB, 48510) > 0.05, // incoming already audible
            $"incoming amplitude was {rendered.ToneAmplitude(FB, 48510):F3}");
    }

    [Fact]
    public void PreviewOfAMissingFile_IsReported_AndTheMainEngineKeepsGoing()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        tc.SetQueueOf(a);
        tc.Engine.Play();

        var preview = tc.Engine.BeginPreview(new Adjacency(
            tc.Track(a), tc.Track(@"C:\missing.wav")));
        Assert.Null(preview);
        Assert.Contains(tc.Notices, n => n.Contains("preview"));
        Assert.Equal(PlaybackState.Playing, tc.Engine.State);
    }
}
