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
}
