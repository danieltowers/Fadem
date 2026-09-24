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
/// while two voices overlap. <see cref="Adding"/> is non-null exactly while an add is running.
/// <see cref="NextAdjacency"/> is the adjacency ⏭ would reach from the current row: into the
/// next row, or under repeat-all from the last row into the first. Repeat-one does not make it
/// the row into itself. Null on the last row with repeat off, and when the queue is empty.
/// </summary>
public sealed record PlayerView(
    long Version,
    long QueueVersion,
    Transport Transport,
    IReadOnlyList<QueueRow> Queue,
    VoiceView? Attended,
    VoiceView? Incoming,
    bool Shuffle,
    RepeatMode Repeat,
    AddProgress? Adding = null,
    Adjacency? NextAdjacency = null);

/// <summary>How far the running add has got: files resolved (queued or rejected) out of every
/// file given to it, including those of an add waiting behind the first.</summary>
public sealed record AddProgress(int Resolved, int Total);

/// <summary>What one <c>Add</c> did: the rows it created and the files it refused, each with the
/// probed shape and the property that disqualified it (ADR 0007). <see cref="Cancelled"/> when
/// <c>CancelAdd</c> ended it early; the rows already added stay.</summary>
public sealed record AddReport(IReadOnlyList<QueueRow> Added, IReadOnlyList<Rejection> Rejected, bool Cancelled = false)
{
    /// <summary>The one notice an add run raises when it refused a file: what was added, every
    /// rejection with its reason, and whether the add was cut short. "All of them were rejected"
    /// gets a sentence too, which v2 left blank. A run that refused nothing raises no notice: its
    /// rows are the news.</summary>
    internal string Summarise()
    {
        var added = Added.Count;
        var rejected = Rejected.Count;
        var tracks = added == 1 ? "1 track" : $"{added} tracks";
        var cancelled = Cancelled ? "Add cancelled. " : "";
        var reasons = string.Join("; ", Rejected.Select(r => $"{System.IO.Path.GetFileName(r.Path)}: {r.Detail}"));
        var head = added == 0
            ? (rejected == 1 ? "Nothing added: the file was rejected" : $"Nothing added: all {rejected} files were rejected")
            : $"Added {tracks}; rejected {rejected}";
        return $"{cancelled}{head} — {reasons} See docs/supported-audio.md.";
    }
}
