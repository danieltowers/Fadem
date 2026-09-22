namespace Faderio.Core.Domain;

/// <summary>The shape of the gain change across a fade.</summary>
public enum FadeCurveKind
{
    EqualPower,
    Linear,
}

/// <summary>A position within a track, measured from the start or from the end.</summary>
public sealed record FadePointSpec(TimeSpan Position, bool FromEnd)
{
    /// <summary>Convenience: a plain position means "measured from the start".</summary>
    public static implicit operator FadePointSpec(TimeSpan position) => new(position, FromEnd: false);
}

/// <summary>
/// An authored transition: four independent, individually optional parameters plus an optional
/// fade curve. Any absent parameter falls back to player defaults (see TransitionResolver).
/// Attached to an adjacency — the ordered pair (outgoing, incoming) — never to a single track.
/// </summary>
public sealed record TransitionSpec
{
    /// <summary>Where the outgoing track begins fading (from start or from end).</summary>
    public FadePointSpec? FadeOutPoint { get; init; }

    /// <summary>Where the incoming track begins playing from.</summary>
    public FadePointSpec? FadeInPoint { get; init; }

    public TimeSpan? FadeOutDuration { get; init; }
    public TimeSpan? FadeInDuration { get; init; }

    /// <summary>Equal-power by default; linear selectable. Individually optional.</summary>
    public FadeCurveKind? Curve { get; init; }

    public bool IsEmpty =>
        FadeOutPoint is null && FadeInPoint is null &&
        FadeOutDuration is null && FadeInDuration is null && Curve is null;
}
