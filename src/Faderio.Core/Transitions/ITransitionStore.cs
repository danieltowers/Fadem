using Faderio.Core.Domain;

namespace Faderio.Core.Transitions;

/// <summary>
/// Persists and looks up transitions by directional track pair, with per-playlist override
/// capability in the schema from Phase 1 (ADR 0002) so the format never has to migrate.
/// </summary>
public interface ITransitionStore
{
    /// <summary>The globally authored transition for the ordered adjacency (outgoing → incoming).</summary>
    TransitionSpec? Get(string outgoingPath, string incomingPath);

    void Upsert(string outgoingPath, string incomingPath, TransitionSpec spec);

    bool Remove(string outgoingPath, string incomingPath);

    /// <summary>Every ordered pair with a global transition, as the store names its tracks
    /// (playlists issue 05).</summary>
    IReadOnlyList<(string Outgoing, string Incoming)> Pairs();

    TransitionSpec? GetOverride(string playlistName, string outgoingPath, string incomingPath);

    /// <summary>Every ordered pair the playlist overrides; none for a playlist with no overrides.</summary>
    IReadOnlyList<(string Outgoing, string Incoming)> OverridePairs(string playlistName);

    void UpsertOverride(string playlistName, string outgoingPath, string incomingPath, TransitionSpec partial);

    bool RemoveOverride(string playlistName, string outgoingPath, string incomingPath);

    /// <summary>Rekeys every override of <paramref name="oldName"/> to <paramref name="newName"/>,
    /// replacing any left under the new name: a playlist rename (D14). Nothing to move is no write.</summary>
    void RenamePlaylist(string oldName, string newName);

    /// <summary>Removes every override of the playlist: a playlist delete (D17). Nothing to remove
    /// is no write.</summary>
    void RemovePlaylist(string playlistName);
}

/// <summary>The store file claims a schema version this build does not understand.</summary>
public sealed class TransitionStoreVersionException : Exception
{
    public int FoundVersion { get; }
    public int SupportedVersion { get; }

    public TransitionStoreVersionException(int foundVersion, int supportedVersion)
        : base($"Transition store schema version {foundVersion} is newer than the supported version {supportedVersion}.")
    {
        FoundVersion = foundVersion;
        SupportedVersion = supportedVersion;
    }
}
