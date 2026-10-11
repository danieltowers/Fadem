using Fadem.Core.Domain;
using Fadem.Core.Settings;

namespace Fadem.Core.Transitions;

/// <summary>
/// Pure resolution: adjacency + store contents + settings → effective transition parameters.
/// Directionality is pinned here: the caller supplies an already-ordered (outgoing, incoming)
/// pair; A→B and B→A are distinct lookups. An authored transition's blank durations play the
/// library's blank duration setting, never the default crossfade's N, which is for unauthored
/// adjacencies alone (build issue 12). Only a blank is read from settings: a value the user put in
/// a transition is never altered by one. An unauthored adjacency resolves to a hard cut, or to the
/// default crossfade when opted in.
/// </summary>
internal sealed class TransitionResolver
{
    public PlayerSettings Settings { get; set; }

    /// <summary>What a blank authored duration resolves to: the blank duration setting.</summary>
    public TimeSpan BlankDuration => TimeSpan.FromSeconds(Settings.BlankDurationSeconds);

    /// <summary>What a blank curve resolves to: the default curve setting.</summary>
    public FadeCurveKind DefaultCurve => Settings.DefaultCurve;

    public TransitionResolver(PlayerSettings settings) => Settings = settings;

    /// <param name="global">The globally authored transition for this adjacency, if any.</param>
    /// <param name="playlistOverride">
    /// A per-playlist override: may specify any subset of parameters; each parameter it omits
    /// falls through to the global transition (ADR 0002).
    /// </param>
    /// <param name="fadeOutAt">
    /// Transition now: the fade-out point moved here, and nothing else. A fade-out that would then
    /// run past the outgoing track's end is shortened, as an authored one is.
    /// </param>
    public EffectiveTransition Resolve(
        TransitionSpec? global,
        TransitionSpec? playlistOverride,
        TimeSpan outgoingDuration,
        TimeSpan incomingDuration,
        TimeSpan? fadeOutAt = null)
    {
        var authored = Merge(global, playlistOverride);

        if (authored is null || authored.IsEmpty)
        {
            return Settings.UnauthoredAdjacency == UnauthoredAdjacency.DefaultCrossfade
                ? DefaultCrossfade(outgoingDuration, fadeOutAt)
                : EffectiveTransition.HardCut;
        }

        var fadeOutDuration = ClampDuration(authored.FadeOutDuration);
        var fadeInDuration = ClampDuration(authored.FadeInDuration);

        // Absent fade-out point: the track begins fading one fade-length before its end.
        var fadeOutPoint = fadeOutAt is { } now
            ? ClampToLength(now, outgoingDuration)
            : authored.FadeOutPoint is { } fp
                ? AbsoluteFadePoint(fp, outgoingDuration)
                : MaxZero(outgoingDuration - fadeOutDuration);

        var fadeInPoint = authored.FadeInPoint is { } ip
            ? ip.Position < TimeSpan.Zero ? TimeSpan.Zero : ip.Position
            : TimeSpan.Zero;

        return new EffectiveTransition
        {
            FadeOutPoint = fadeOutPoint,
            FadeOutDuration = ShortenToEnd(fadeOutPoint, fadeOutDuration, outgoingDuration),
            FadeInPoint = fadeInPoint,
            FadeInDuration = fadeInDuration,
            Curve = authored.Curve ?? DefaultCurve,
        };
    }

    private EffectiveTransition DefaultCrossfade(TimeSpan outgoingDuration, TimeSpan? fadeOutAt)
    {
        var d = TimeSpan.FromSeconds(Settings.DefaultCrossfadeSeconds);
        var point = fadeOutAt is { } now ? ClampToLength(now, outgoingDuration) : MaxZero(outgoingDuration - d);
        return new EffectiveTransition
        {
            FadeOutPoint = point,
            FadeOutDuration = ShortenToEnd(point, d, outgoingDuration),
            FadeInPoint = TimeSpan.Zero,
            FadeInDuration = d,
            Curve = DefaultCurve,
        };
    }

    /// <summary>The override laid over the global transition, parameter by parameter; either alone
    /// when the other is absent.</summary>
    internal static TransitionSpec? Merge(TransitionSpec? global, TransitionSpec? playlistOverride)
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
        var d = duration ?? BlankDuration;
        return d < TimeSpan.Zero ? TimeSpan.Zero : d;
    }

    /// <summary>The fade-out cannot extend beyond the end of the outgoing track: the
    /// <em>duration</em> is shortened, the point is kept exactly where it was put.</summary>
    private static TimeSpan ShortenToEnd(TimeSpan fadeOutPoint, TimeSpan fadeOutDuration, TimeSpan length) =>
        fadeOutPoint + fadeOutDuration > length ? MaxZero(length - fadeOutPoint) : fadeOutDuration;

    private static TimeSpan AbsoluteFadePoint(FadePointSpec point, TimeSpan length) =>
        ClampToLength(point.FromEnd ? length - point.Position : point.Position, length);

    private static TimeSpan ClampToLength(TimeSpan point, TimeSpan length)
    {
        if (point < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }
        return point > length ? length : point;
    }

    private static TimeSpan MaxZero(TimeSpan t) => t < TimeSpan.Zero ? TimeSpan.Zero : t;
}
