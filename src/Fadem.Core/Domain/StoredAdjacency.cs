namespace Fadem.Core.Domain;

/// <summary>
/// One adjacency the store holds a transition for, as the transition manager lists it for one
/// track (playlists issue 05, D24): the adjacency, whether that track is its outgoing side, and
/// its layers as <c>Transition</c> gives them. A track into itself is listed once, as outgoing.
/// </summary>
public sealed record StoredAdjacency(Adjacency Adjacency, bool IsOutgoing, TransitionLayers Layers)
{
    /// <summary>The track at the adjacency's other side: the incoming one when the listed track is
    /// outgoing, else the outgoing one.</summary>
    public Track Other => IsOutgoing ? Adjacency.Incoming : Adjacency.Outgoing;

    /// <summary>The open playlist overrides an adjacency with no global transition (D22): only
    /// that playlist plays it.</summary>
    public bool PlaylistOnly => Layers.Global is not { IsBlank: false } && Layers.Override is not null;
}
