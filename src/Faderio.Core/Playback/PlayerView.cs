using Faderio.Core.Domain;
using Faderio.Core.Queue;

namespace Faderio.Core.Playback;

/// <summary>Engine-issued identity of a queue row; stable across shuffle. A stale id is a
/// <see cref="IEngineSink.Notice"/>, never an exception.</summary>
public readonly record struct RowId(long Value)
{
    public override string ToString() => $"row {Value}";
}

/// <summary>One row of the queue as the shell sees it. <see cref="IsCurrent"/> follows the
/// attended voice: it flips at the audible handover, not when the incoming voice is spawned.</summary>
public sealed record QueueRow(RowId Id, Track Track, bool IsCurrent);

/// <summary>One voice as the shell sees it: which row, where the listener is within it, and how
/// long it is. <see cref="Playhead"/> is audible time, derived from the frames the device has
/// emitted (ADR 0001).</summary>
public sealed record VoiceView(QueueRow Row, Playhead Playhead, Length Length)
{
    /// <summary>Never negative: <c>Playhead ≤ Length</c> by construction.</summary>
    public Length Remaining => new(Length.TimeSpan - Playhead.TimeSpan);
}

/// <summary>
/// The whole player at one instant: immutable, complete, render-from-able. No live collection
/// crosses the seam (ADR 0003). <see cref="Queue"/> is a fresh list per <see cref="QueueVersion"/>;
/// <see cref="Attended"/> is the voice the listener attends to — the outgoing voice until it
/// falls silent — and is null exactly when stopped; <see cref="Incoming"/> is non-null only
/// while two voices overlap.
/// </summary>
public sealed record PlayerView(
    long Version,
    long QueueVersion,
    Transport Transport,
    IReadOnlyList<QueueRow> Queue,
    VoiceView? Attended,
    VoiceView? Incoming,
    bool Shuffle,
    RepeatMode Repeat);

/// <summary>What <c>AddTracks</c> did: the rows it created and the files it refused, each with
/// the probed shape and the property that disqualified it (ADR 0007). The shell formats these
/// and points at <c>docs/supported-audio.md</c>.</summary>
public sealed record AddReport(IReadOnlyList<QueueRow> Added, IReadOnlyList<Rejection> Rejected);
