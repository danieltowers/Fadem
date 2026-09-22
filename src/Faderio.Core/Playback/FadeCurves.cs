using Faderio.Core.Domain;

namespace Faderio.Core.Playback;

/// <summary>
/// Fade curve gain functions. <paramref name="t"/> runs 0→1 across the fade.
/// Equal-power crossfades use sin/cos quadrature so combined loudness stays roughly constant
/// mid-overlap (a linear crossfade audibly dips); linear is a straight 0→full / full→0 ramp.
/// </summary>
public static class FadeCurves
{
    public static double FadeInGain(FadeCurveKind kind, double t) => kind switch
    {
        FadeCurveKind.Linear => t,
        _ => Math.Sin(t * Math.PI / 2),
    };

    public static double FadeOutGain(FadeCurveKind kind, double t) => kind switch
    {
        FadeCurveKind.Linear => 1 - t,
        _ => Math.Cos(t * Math.PI / 2),
    };
}
