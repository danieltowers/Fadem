using Faderio.Core.Domain;
using Faderio.Core.Settings;
using Faderio.Core.Transitions;

namespace Faderio.Core.Tests;

public class TransitionResolverTests
{
    private static PlayerSettings Settings(
        DefaultAdjacencyBehaviour behaviour = DefaultAdjacencyBehaviour.HardCut,
        double defaultCrossfadeSeconds = 5.0) =>
        new()
        {
            DefaultAdjacencyBehaviour = behaviour,
            DefaultCrossfadeDuration = TimeSpan.FromSeconds(defaultCrossfadeSeconds),
        };

    [Fact]
    public void UnauthoredAdjacency_ResolvesToHardCutByDefault()
    {
        var resolver = new TransitionResolver(Settings());
        var result = resolver.Resolve(null, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        Assert.True(result.IsHardCut);
    }

    [Fact]
    public void UnauthoredAdjacency_WithGlobalDefaultCrossfadeSetting_UsesDefaultDuration()
    {
        var resolver = new TransitionResolver(Settings(DefaultAdjacencyBehaviour.GlobalDefaultCrossfade, 7));
        var result = resolver.Resolve(null, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        Assert.False(result.IsHardCut);
        Assert.Equal(TimeSpan.FromSeconds(3), result.FadeOutPoint); // 10 − 7
        Assert.Equal(TimeSpan.FromSeconds(7), result.FadeOutDuration);
        Assert.Equal(TimeSpan.Zero, result.FadeInPoint);
        Assert.Equal(TimeSpan.FromSeconds(7), result.FadeInDuration);
        Assert.Equal(FadeCurveKind.EqualPower, result.Curve);
    }

    [Fact]
    public void IndividuallyOptionalParameters_FallBackToDefaults()
    {
        var resolver = new TransitionResolver(Settings());
        // Only the fade-in point is authored: everything else defaults.
        var spec = new TransitionSpec { FadeInPoint = new FadePointSpec(TimeSpan.FromSeconds(2), FromEnd: false) };
        var result = resolver.Resolve(spec, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(12));
        Assert.False(result.IsHardCut);
        Assert.Equal(TimeSpan.FromSeconds(2), result.FadeInPoint);
        Assert.Equal(TimeSpan.FromSeconds(5), result.FadeInDuration);
        Assert.Equal(TimeSpan.FromSeconds(5), result.FadeOutDuration);
        Assert.Equal(TimeSpan.FromSeconds(5), result.FadeOutPoint); // 10 − 5
        Assert.Equal(FadeCurveKind.EqualPower, result.Curve);
    }

    [Fact]
    public void FromEndFlag_MeasuresFromTrackEnd()
    {
        var resolver = new TransitionResolver(Settings());
        var spec = new TransitionSpec { FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1.5), FromEnd: true) };
        var result = resolver.Resolve(spec, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(8.5), result.FadeOutPoint);
    }

    [Fact]
    public void Curve_IsIndividuallyOptional_AndDefaultsToEqualPower()
    {
        var resolver = new TransitionResolver(Settings());
        var spec = new TransitionSpec { FadeInDuration = TimeSpan.FromSeconds(2) };
        Assert.Equal(FadeCurveKind.EqualPower, resolver.Resolve(spec, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)).Curve);

        var linear = new TransitionSpec { FadeInDuration = TimeSpan.FromSeconds(2), Curve = FadeCurveKind.Linear };
        Assert.Equal(FadeCurveKind.Linear, resolver.Resolve(linear, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)).Curve);
    }

    [Fact]
    public void PlaylistOverride_FillsPerParameter_WithFallThrough()
    {
        var resolver = new TransitionResolver(Settings());
        var global = new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(3), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(1),
            FadeInPoint = new FadePointSpec(TimeSpan.FromSeconds(4), FromEnd: false),
            Curve = FadeCurveKind.Linear,
        };
        // The override specifies only the fade-out duration: everything else falls through.
        var playlistOverride = new TransitionSpec { FadeOutDuration = TimeSpan.FromSeconds(2) };
        var result = resolver.Resolve(global, playlistOverride, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(3), result.FadeOutPoint);
        Assert.Equal(TimeSpan.FromSeconds(2), result.FadeOutDuration); // overridden
        Assert.Equal(TimeSpan.FromSeconds(4), result.FadeInPoint);
        Assert.Equal(TimeSpan.FromSeconds(5), result.FadeInDuration); // from defaults
        Assert.Equal(FadeCurveKind.Linear, result.Curve);
    }

    [Fact]
    public void FadeOutIsClampedToTheTrackEnd()
    {
        var resolver = new TransitionResolver(Settings());
        var spec = new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(9), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(4),
        };
        var result = resolver.Resolve(spec, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(9), result.FadeOutPoint);
        Assert.Equal(TimeSpan.FromSeconds(1), result.FadeOutDuration);
    }

    [Fact]
    public void AuthoredFadeInPoint_CannotBeNegative()
    {
        var resolver = new TransitionResolver(Settings());
        var spec = new TransitionSpec { FadeInPoint = new FadePointSpec(TimeSpan.FromSeconds(-3), FromEnd: false) };
        var result = resolver.Resolve(spec, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.Zero, result.FadeInPoint);
    }

    // ── Transition now: the fade-out point moved to the playhead ────────────────────────

    [Fact]
    public void TransitionNow_MovesOnlyTheFadeOutPoint_AndKeepsTheAuthoredDuration()
    {
        var resolver = new TransitionResolver(Settings());
        var spec = new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(9.5), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(2), // clamped to 0.5 s at the authored point
            FadeInPoint = new FadePointSpec(TimeSpan.FromSeconds(3), FromEnd: false),
            FadeInDuration = TimeSpan.FromSeconds(4),
            Curve = FadeCurveKind.Linear,
        };
        var result = resolver.Resolve(
            spec, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), fadeOutAt: TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromSeconds(2), result.FadeOutPoint);
        Assert.Equal(TimeSpan.FromSeconds(2), result.FadeOutDuration);
        Assert.Equal(TimeSpan.FromSeconds(3), result.FadeInPoint);
        Assert.Equal(TimeSpan.FromSeconds(4), result.FadeInDuration);
        Assert.Equal(FadeCurveKind.Linear, result.Curve);
    }

    [Fact]
    public void TransitionNow_ShortensAFadeOutThatWouldRunPastTheLength()
    {
        var resolver = new TransitionResolver(Settings());
        var spec = new TransitionSpec { FadeOutDuration = TimeSpan.FromSeconds(4) };
        var result = resolver.Resolve(
            spec, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), fadeOutAt: TimeSpan.FromSeconds(8.5));

        Assert.Equal(TimeSpan.FromSeconds(8.5), result.FadeOutPoint);
        Assert.Equal(TimeSpan.FromSeconds(1.5), result.FadeOutDuration);
    }

    [Fact]
    public void TransitionNow_OnAnUnauthoredAdjacency_StartsTheDefaultCrossfadeThere_WhenItIsOn()
    {
        var resolver = new TransitionResolver(Settings(DefaultAdjacencyBehaviour.GlobalDefaultCrossfade, 3));
        var result = resolver.Resolve(
            null, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), fadeOutAt: TimeSpan.FromSeconds(4));

        Assert.False(result.IsHardCut);
        Assert.Equal(TimeSpan.FromSeconds(4), result.FadeOutPoint);
        Assert.Equal(TimeSpan.FromSeconds(3), result.FadeOutDuration);
        Assert.Equal(TimeSpan.FromSeconds(3), result.FadeInDuration);
    }

    [Fact]
    public void TransitionNow_OnAnUnauthoredAdjacency_IsStillAHardCut_ByDefault()
    {
        var resolver = new TransitionResolver(Settings());
        var result = resolver.Resolve(
            null, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), fadeOutAt: TimeSpan.FromSeconds(4));

        Assert.True(result.IsHardCut);
    }
}
