using Fadem.Core.Domain;

namespace Fadem.Core.Tests;

/// <summary>Crossfades and fade curves (issue 0007): envelopes pinned through the seam.</summary>
public sealed class CrossfadeShapeTests
{
    private const double FA = 1000;
    private const double FB = 2000;

    private static TransitionSpec Crossfade(double duration, FadeCurveKind? curve = null) => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
        FadeInPoint = TimeSpan.Zero,
        FadeOutDuration = TimeSpan.FromSeconds(duration),
        FadeInDuration = TimeSpan.FromSeconds(duration),
        Curve = curve,
    };

    /// <summary>Normalised gains of both voices at a frame, via frequency separation.</summary>
    private static (double GA, double GB) Gains(RenderedAudio rendered, long endFrame) =>
        (rendered.ToneAmplitude(FA, endFrame) / 0.4, rendered.ToneAmplitude(FB, endFrame) / 0.4);

    [Fact]
    public void Crossfade_BothTracksRenderSimultaneously()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 4, FA);
        var b = TestAudio.WriteToneWav("b.wav", 4, FB);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, Crossfade(1.0));
        tc.Engine.Play();
        tc.Output.Advance(44100 * 2);

        var (gA, gB) = Gains(tc.Rendered, 66150); // 1.5 s, mid-crossfade
        Assert.True(gA > 0.5, $"outgoing should be audible mid-crossfade (was {gA})");
        Assert.True(gB > 0.2, $"incoming should be audible mid-crossfade (was {gB})");
    }

    [Theory]
    [InlineData(null)] // absent curve falls back to equal-power
    [InlineData(FadeCurveKind.EqualPower)]
    public void EqualPower_KeepsCombinedPowerRoughlyConstant(FadeCurveKind? curve)
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 4, FA);
        var b = TestAudio.WriteToneWav("b.wav", 4, FB);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, Crossfade(1.0, curve));
        tc.Engine.Play();
        tc.Output.Advance(44100 * 2);

        // sin² + cos² = 1 at every point of the overlap.
        foreach (var fraction in new[] { 0.25, 0.5, 0.75 })
        {
            var frame = 44100 + (long)(22050 * fraction);
            var (gA, gB) = Gains(tc.Rendered, frame);
            Assert.InRange(gA * gA + gB * gB, 0.85, 1.15);
        }
    }

    [Fact]
    public void LinearCurve_IsSelectable_AndDipsMidOverlap()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 4, FA);
        var b = TestAudio.WriteToneWav("b.wav", 4, FB);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, Crossfade(1.0, FadeCurveKind.Linear));
        tc.Engine.Play();
        tc.Output.Advance(44100 * 2);

        // Linear ramps: gA = 1 − t, gB = t, evaluated mid-overlap (t = 0.5).
        var frame = 44100 + 22050;
        var (gA, gB) = Gains(tc.Rendered, frame);
        Assert.InRange(gA, 0.4, 0.6);
        Assert.InRange(gB, 0.4, 0.6);
        Assert.True(gA * gA + gB * gB < 0.8, "a linear crossfade must dip in loudness mid-overlap");
    }

    [Fact]
    public void SCurve_KeepsCombinedPowerConstant_ButEasesInAndOut()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 4, FA);
        var b = TestAudio.WriteToneWav("b.wav", 4, FB);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, Crossfade(1.0, FadeCurveKind.SCurve));
        tc.Engine.Play();
        tc.Output.Advance(44100 * 3);

        // Still equal-power: sin² + cos² = 1 on the eased t.
        foreach (var t in new[] { 0.1, 0.25, 0.5, 0.75, 0.9 })
        {
            var (gA, gB) = Gains(tc.Rendered, 44100 + (long)(44100 * t));
            Assert.InRange(gA * gA + gB * gB, 0.85, 1.15);
        }

        // Eased: at t = 0.1 the incoming is at ~0.04, where equal-power has it at ~0.16; at
        // t = 0.9 the outgoing is as near silent. Mid-overlap both sit at cos(π/4) ≈ 0.707.
        var (earlyA, earlyB) = Gains(tc.Rendered, 44100 + 4410);
        Assert.InRange(earlyB, 0.0, 0.08);
        Assert.InRange(earlyA, 0.95, 1.05);
        var (lateA, _) = Gains(tc.Rendered, 44100 + 39690);
        Assert.InRange(lateA, 0.0, 0.08);
        var (midA, midB) = Gains(tc.Rendered, 44100 + 22050);
        Assert.InRange(midA, 0.6, 0.8);
        Assert.InRange(midB, 0.6, 0.8);
    }

    [Fact]
    public void MixedOverlap_IncomingAlreadyAtFull_WhileOutgoingFades()
    {
        // d_i = 0: the incoming track plays at full volume while the outgoing fades.
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 4, FA);
        var b = TestAudio.WriteToneWav("b.wav", 4, FB);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(1),
            FadeInDuration = TimeSpan.Zero,
        });
        tc.Engine.Play();
        tc.Output.Advance(44100 * 2);

        // At 1.5 s: incoming at full (1.0), outgoing at cos(π/4) ≈ 0.707.
        var (gA, gB) = Gains(tc.Rendered, 66150);
        Assert.InRange(gB, 0.9, 1.05);
        Assert.InRange(gA, 0.6, 0.8);
    }
}
