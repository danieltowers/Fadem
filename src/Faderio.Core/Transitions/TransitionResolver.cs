using Faderio.Core.Domain;
using Faderio.Core.Settings;

namespace Faderio.Core.Transitions;

/// <summary>
/// Pure resolution: adjacency + store contents + settings → effective transition parameters.
/// Directionality is pinned here: the caller supplies an already-ordered (outgoing, incoming)
/// pair; A→B and B→A are distinct lookups. Absent parameters fall through per-parameter to
/// player defaults; an unauthored adjacency resolves to the player's default adjacency
/// behaviour (hard cut, or a global default crossfade when opted in).
/// </summary>
public sealed class TransitionResolver
{
    public PlayerSettings Settings { get; set; }

    public TransitionResolver(PlayerSettings settings) => Settings = settings;

    /// <param name="global">The globally authored transition for this adjacency, if any.</param>
    /// <param name="playlistOverride">
    /// A per-playlist override: may specify any subset of parameters; each parameter it omits
    /// falls through to the global transition (ADR 0002).
    /// </param>
    public EffectiveTransition Resolve(
        TransitionSpec? global,
        TransitionSpec? playlistOverride,
        TimeSpan outgoingDuration,
        TimeSpan incomingDuration)
    {
        var authored = Merge(global, playlistOverride);

        if (authored is null || authored.IsEmpty)
        {
            return Settings.DefaultAdjacencyBehaviour == DefaultAdjacencyBehaviour.GlobalDefaultCrossfade
                ? DefaultCrossfade(outgoingDuration)
                : EffectiveTransition.HardCut;
        }

        var fadeOutDuration = ClampDuration(authored.FadeOutDuration);
        var fadeInDuration = ClampDuration(authored.FadeInDuration);

        // Absent fade-out point: the track begins fading one fade-length before its end.
        var fadeOutPoint = authored.FadeOutPoint is { } fp
            ? AbsoluteFadePoint(fp, outgoingDuration)
            : MaxZero(outgoingDuration - fadeOutDuration);

        var fadeInPoint = authored.FadeInPoint is { } ip
            ? ip.Position < TimeSpan.Zero ? TimeSpan.Zero : ip.Position
            : TimeSpan.Zero;

        // The fade-out cannot extend beyond the end of the outgoing track: the *duration*
        // is clamped, the authored point is kept exactly where it was put.
        if (fadeOutPoint + fadeOutDuration > outgoingDuration)
        {
            fadeOutDuration = MaxZero(outgoingDuration - fadeOutPoint);
        }

        return new EffectiveTransition
        {
            FadeOutPoint = fadeOutPoint,
            FadeOutDuration = fadeOutDuration,
            FadeInPoint = fadeInPoint,
            FadeInDuration = fadeInDuration,
            Curve = authored.Curve ?? FadeCurveKind.EqualPower,
        };
    }

    private EffectiveTransition DefaultCrossfade(TimeSpan outgoingDuration)
    {
        var d = ClampDuration(null);
        return new EffectiveTransition
        {
            FadeOutPoint = MaxZero(outgoingDuration - d),
            FadeOutDuration = d,
            FadeInPoint = TimeSpan.Zero,
            FadeInDuration = d,
            Curve = FadeCurveKind.EqualPower,
        };
    }

    private static TransitionSpec? Merge(TransitionSpec? global, TransitionSpec? playlistOverride)
    {
        if (global is null)
        {
            return playlistOverride;
        }
        if (playlistOverride is null)
        {
            return global;
        }

        // Per-parameter: every parameter the override omits falls through to the global transition.
        return new TransitionSpec
        {
            FadeOutPoint = playlistOverride.FadeOutPoint ?? global.FadeOutPoint,
            FadeInPoint = playlistOverride.FadeInPoint ?? global.FadeInPoint,
            FadeOutDuration = playlistOverride.FadeOutDuration ?? global.FadeOutDuration,
            FadeInDuration = playlistOverride.FadeInDuration ?? global.FadeInDuration,
            Curve = playlistOverride.Curve ?? global.Curve,
        };
    }

    private TimeSpan ClampDuration(TimeSpan? duration)
    {
        var d = duration ?? Settings.DefaultCrossfadeDuration;
        return d < TimeSpan.Zero ? TimeSpan.Zero : d;
    }

    private static TimeSpan AbsoluteFadePoint(FadePointSpec point, TimeSpan trackDuration)
    {
        var absolute = point.FromEnd
            ? trackDuration - point.Position
            : point.Position;
        if (absolute < TimeSpan.Zero)
        {
            absolute = TimeSpan.Zero;
        }
        if (absolute > trackDuration)
        {
            absolute = trackDuration;
        }
        return absolute;
    }

    private static TimeSpan MaxZero(TimeSpan t) => t < TimeSpan.Zero ? TimeSpan.Zero : t;
}
