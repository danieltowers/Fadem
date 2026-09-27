using Faderio.Core.Transitions;

namespace Faderio.Core.Domain;

/// <summary>Which layer of an adjacency's transition the editor writes: the global transition, or
/// the open playlist's override of it (D21).</summary>
public enum TransitionLayer
{
    Global,
    Playlist,
}

/// <summary>
/// An adjacency's transition as the store holds it, layer by layer (playlists issue 04): the
/// global transition, the open playlist's override of it, and what the two merge to, each
/// parameter the override omits falling through to the global (ADR 0005). An override with no
/// global is a playlist-only transition (D22): the effective transition is the override alone.
/// </summary>
public sealed record TransitionLayers(TransitionSpec? Global, TransitionSpec? Override, TransitionSpec? Effective)
{
    public static TransitionLayers None { get; } = new(null, null, null);

    /// <summary>The open playlist plays something other than the global transition.</summary>
    public bool Differ => Effective != Global;

    /// <summary>What a blank fade-out duration plays when <paramref name="layer"/> is saved: in the
    /// playlist layer the global transition's, where it sets one; else the fixed default for a blank
    /// authored duration (build issue 12).</summary>
    public TimeSpan BlankFadeOutDuration(TransitionLayer layer) =>
        (layer == TransitionLayer.Playlist ? Global?.FadeOutDuration : null) ?? TransitionResolver.BlankAuthoredDuration;

    /// <summary>What a blank fade-in duration plays: as <see cref="BlankFadeOutDuration"/>.</summary>
    public TimeSpan BlankFadeInDuration(TransitionLayer layer) =>
        (layer == TransitionLayer.Playlist ? Global?.FadeInDuration : null) ?? TransitionResolver.BlankAuthoredDuration;
}
