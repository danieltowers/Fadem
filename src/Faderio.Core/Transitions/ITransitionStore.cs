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

    /// <summary>Any authored transition whose outgoing track is <paramref name="outgoingPath"/>.
    /// Used to honour an authored fade-out at the end of the queue, where there is no
    /// incoming track (spec story 19).</summary>
    TransitionSpec? FindForOutgoing(string outgoingPath);

    TransitionSpec? GetOverride(string playlistName, string outgoingPath, string incomingPath);

    void UpsertOverride(string playlistName, string outgoingPath, string incomingPath, TransitionSpec partial);

    bool RemoveOverride(string playlistName, string outgoingPath, string incomingPath);
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
