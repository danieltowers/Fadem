using Fadem.Core.Domain;
using Fadem.Core.Playback;

namespace Fadem.Core.Tests;

/// <summary>
/// Sample-accurate fade engine through the seam (issue 0006). Authored points must land
/// within one sample frame of where authored; single-sample assertions pin exact frames.
/// </summary>
public sealed class FadeEngineAccuracyTests
{
    private const double FA = 1000;   // outgoing tone
    private const double FB = 2000;   // incoming tone

    /// <summary>A→B: fade-out from 1.0s over 0.5s; incoming from 0.5s fading in over 0.25s.</summary>
    internal static TransitionSpec CrossfadeSpec() => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
        FadeInPoint = new FadePointSpec(TimeSpan.FromSeconds(0.5), FromEnd: false),
        FadeOutDuration = TimeSpan.FromSeconds(0.5),
        FadeInDuration = TimeSpan.FromSeconds(0.25),
    };

    [Fact]
    public void AuthoredPoints_LandWithinOneSampleFrame()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 4, FA);
        var b = TestAudio.WriteToneWav("b.wav", 4, FB);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, CrossfadeSpec());
        tc.Engine.Play();
        tc.Output.Advance(44100 * 2);

        const long trigger = 44100;      // authored fade-out point, 1.0 s
        const long fadeOut = 22050;      // 0.5 s
        const long fadeIn = 11025;       // 0.25 s

        double Expected(long frame)
        {
            double gA = 1.0, gB = 0.0;
            if (frame >= trigger)
            {
                var into = frame - trigger;
                gA = into < fadeOut
                    ? FadeCurves.FadeOutGain(FadeCurveKind.EqualPower, into / (double)fadeOut)
                    : 0.0;
                gB = into < fadeIn
                    ? FadeCurves.FadeInGain(FadeCurveKind.EqualPower, into / (double)fadeIn)
                    : 1.0;
            }
            var wA = 2.0 * Math.PI * FA / TestAudio.SampleRate;
            var wB = 2.0 * Math.PI * FB / TestAudio.SampleRate;
            // A plays from its start; B plays from exactly its authored 0.5 s fade-in point.
            return 0.4 * gA * Math.Sin(wA * frame) + 0.4 * gB * Math.Sin(wB * (frame - trigger + 22050));
        }

        // One frame before the trigger: the outgoing track is untouched, the incoming silent.
        Assert.Equal(Expected(trigger - 1), tc.Rendered.Sample(trigger - 1, 0), 2);

        // Mid-crossfade (frame 55125): outgoing at cos(π/4), incoming fully faded in — from
        // exactly the authored fade-in point of the incoming track.
        Assert.Equal(Expected(55125), tc.Rendered.Sample(55125, 0), 2);

        // One frame after the outgoing fade completes: only the incoming track remains.
        Assert.Equal(Expected(trigger + fadeOut + 1), tc.Rendered.Sample(trigger + fadeOut + 1, 0), 2);
    }

    [Fact]
    public void FadeOutPointFromStart_IsHonoured_Exactly()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1.5), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(1),
            FadeInPoint = TimeSpan.Zero,
            FadeInDuration = TimeSpan.FromSeconds(1),
        });
        tc.Engine.Play();
        tc.Output.Advance(44100 * 3);

        // The frame before the fade-out point is still at full level.
        Assert.Equal(0.4, tc.Rendered.Sample(66149, 0), 2);
        // Halfway through the fade: 0.4·cos(π/4) + 0.2·sin(π/4) ≈ 0.424 (equal-power).
        var half = tc.Rendered.Sample(66149 + 22050, 0);
        Assert.InRange(half, 0.38, 0.46);
    }

    [Fact]
    public void FadeOutPointFromEnd_IsHonoured_Exactly()
    {
        using var tc = TestEngine.Create();
        // A is 4 s; fade-out point measured from the end: 1 s before the end → 3.0 s.
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: true),
            FadeOutDuration = TimeSpan.FromSeconds(0.5),
            FadeInPoint = TimeSpan.Zero,
            FadeInDuration = TimeSpan.FromSeconds(0.5),
        });
        tc.Engine.Play();
        tc.Output.Advance(44100 * 4);

        // Full level right up to 3.0 s (4.0 − 1.0), the authored fade-out point.
        Assert.Equal(0.4, tc.Rendered.Sample(132299, 0), 2);
        // 0.1 s into the fade: 0.4·cos(0.1π) + 0.2·sin(0.1π) ≈ 0.442 — the fade is running
        // from exactly the point measured from the track's end.
        Assert.InRange(tc.Rendered.Sample(136710, 0), 0.42, 0.47);
    }

    [Fact]
    public void IncomingFadeInPoint_LandsSampleAccurately()
    {
        // The incoming track is silent for its first 1.0 s, then a 2 kHz tone; it starts
        // playing from the authored 0.5 s fade-in point, so the tone may only become audible
        // 0.5 s after the trigger.
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 4, FA);
        var b = TestAudio.WriteWav("b.wav", 4,
            f => f < 44100 ? 0 : 0.4 * Math.Sin(2 * Math.PI * FB * (f - 44100) / TestAudio.SampleRate));
        tc.Add(a, b);
        tc.Store.Upsert(a, b, CrossfadeSpec());
        tc.Engine.Play();
        tc.Output.Advance(44100 * 2);

        // Trigger at 1.0 s + 0.5 s of B's silent intro = 1.5 s: no B tone before that.
        Assert.True(tc.Rendered.ToneAmplitude(FB, 66150) < 0.05,
            $"B tone must be silent until 1.5 s (was {tc.Rendered.ToneAmplitude(FB, 66150):F3})");
        // Just after: the tone is there, faded in.
        Assert.True(tc.Rendered.ToneAmplitude(FB, 66150 + 2000) > 0.1);
    }
}
