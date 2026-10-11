using Fadem.Core.Domain;

namespace Fadem.Core.Playback;

/// <summary>
/// Fade curve gain functions. <paramref name="t"/> runs 0→1 across the fade.
/// Equal-power crossfades use sin/cos quadrature so combined loudness stays roughly constant
/// mid-overlap (a linear crossfade audibly dips); linear is a straight 0→full / full→0 ramp.
/// The S-curve is equal-power run on an eased t, (1 − cos πt)/2: the same constant power, but
/// flat at both ends, so the incoming track creeps in and the outgoing one tails off.
/// </summary>
internal static class FadeCurves
{
    public static double FadeInGain(FadeCurveKind kind, double t) => kind switch
    {
        FadeCurveKind.Linear => t,
        FadeCurveKind.SCurve => Math.Sin(Eased(t) * Math.PI / 2),
        _ => Math.Sin(t * Math.PI / 2),
    };

    public static double FadeOutGain(FadeCurveKind kind, double t) => kind switch
    {
        FadeCurveKind.Linear => 1 - t,
        FadeCurveKind.SCurve => Math.Cos(Eased(t) * Math.PI / 2),
        _ => Math.Cos(t * Math.PI / 2),
    };

    private static double Eased(double t) => (1 - Math.Cos(t * Math.PI)) / 2;
}
