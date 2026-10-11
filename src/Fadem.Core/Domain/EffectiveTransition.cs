namespace Fadem.Core.Domain;

/// <summary>
/// The fully resolved transition at a boundary: every parameter present, defaults filled in,
/// ready to be turned into sample positions by the engine. An adjacency with no transition
/// (and no global default crossfade setting) resolves to <see cref="HardCut"/>.
/// </summary>
internal sealed record EffectiveTransition
{
    public bool IsHardCut { get; init; }

    /// <summary>Absolute position within the outgoing track (from its start) where its fade begins.</summary>
    public TimeSpan FadeOutPoint { get; init; }

    public TimeSpan FadeOutDuration { get; init; }

    /// <summary>Position within the incoming track where it begins playing from.</summary>
    public TimeSpan FadeInPoint { get; init; }

    public TimeSpan FadeInDuration { get; init; }

    public FadeCurveKind Curve { get; init; } = FadeCurveKind.EqualPower;

    public static EffectiveTransition HardCut { get; } = new() { IsHardCut = true };
}
