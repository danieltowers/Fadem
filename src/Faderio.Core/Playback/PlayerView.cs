using Faderio.Core.Domain;
using Faderio.Core.Queue;
using Faderio.Core.Settings;

namespace Faderio.Core.Playback;

/// <summary>Engine-issued identity of a queue row; stable across shuffle. A stale id is a
/// <see cref="IEngineSink.Notice"/>, never an exception.</summary>
public readonly record struct RowId(long Value)
{
    public override string ToString() => $"row {Value}";
}

/// <summary>One row of the queue as the shell sees it. <see cref="IsCurrent"/> follows the
/// attended voice: it flips at the audible handover, not when the incoming voice is spawned.
/// <see cref="IsUnreadable"/> is set when the row's file could not be opened the last time the
/// queue reached it, and clears the next time it opens (build issue 10). <see cref="NextAuthored"/>
/// is whether the row's this → next, the <see cref="RowAdjacencies.Next"/> the editor opens from
/// it, has a transition in the store: null where the row has none, and in an
/// <see cref="AddReport"/> (interim transport 07).</summary>
public sealed record QueueRow(RowId Id, Track Track, bool IsCurrent, bool IsUnreadable = false, bool? NextAuthored = null);

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
/// next row, or under repeat-all from the last row into the first, passing over rows marked
/// unreadable. Repeat-one does not make it the row into itself. Null on the last row with repeat
/// off, when every row after is marked, and when the queue is empty.
/// <see cref="CanTransitionNow"/> is whether <c>TransitionNow</c> would act: playing, one voice
/// heard alone with no handover or finish still in the device, and a next adjacency.
/// <see cref="Settings"/> are the settings in force, as last saved: the output mode among them is
/// the one chosen for the next launch, not necessarily the one in use (build issue 12).
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
    Adjacency? NextAdjacency = null,
    bool CanTransitionNow = false)
{
    public PlayerSettings Settings { get; init; } = new();

    /// <summary>The preview running in the editor, or null (build issue 16). While it runs the
    /// queue is held: <see cref="Attended"/>, <see cref="Incoming"/> and <see cref="Transport"/>
    /// stay the queue's, paused where it was held, or stopped.</summary>
    public PreviewView? Preview { get; init; }

    /// <summary>The clip light (build issue 17): the clips heard since it was last cleared, or null.
    /// A clip is latched when the listener hears it, not when it is rendered.</summary>
    public ClipView? Clip { get; init; }

    /// <summary>The library open, or null before any library has been opened (ADR 0011).</summary>
    public LibraryView? Library { get; init; }
}

/// <summary>The open library as the shell sees it: its folder, the folder's leaf name, and
/// whether it opened. An unavailable library holds no store: authoring is refused with a notice,
/// and what is queued still plays.</summary>
public sealed record LibraryView(string Path, string Name, bool IsAvailable);

/// <summary>The clips heard since the light was last cleared (build issue 17): how many, and the
/// cause of the latest, which is what the listener most likely just heard.</summary>
public sealed record ClipView(int Count, ClipCause Latest);

/// <summary>What a clip is attributed to: the adjacency whose transition was in flight, when two
/// voices overlapped at the clipped sample, or else the attended track, the one voice heard; a
/// preview's alike. Named by tracks, not rows, so it stays valid after its rows are removed.</summary>
public sealed record ClipCause
{
    private ClipCause(Adjacency? transition, Track? track)
    {
        Transition = transition;
        Track = track;
    }

    /// <summary>The adjacency in flight, or null when one track clipped alone.</summary>
    public Adjacency? Transition { get; }

    /// <summary>The attended track, which clipped alone, or null when a transition was in flight.</summary>
    public Track? Track { get; }

    public static ClipCause Of(Adjacency transition) => new(transition, null);

    public static ClipCause Of(Track track) => new(null, track);

    /// <summary>How the cause is named to the user: the adjacency as it names itself, or the
    /// track's file name without extension.</summary>
    public string Name => Transition?.Name ?? Path.GetFileNameWithoutExtension(Track?.FilePath) ?? "";
}

/// <summary>Where a preview is: the outgoing track before its transition begins, the transition
/// until its later fade completes, then the tail after it.</summary>
public enum PreviewPhase
{
    PreRoll,
    Transition,
    Tail,
}

/// <summary>A running preview as the editor shows it (build issue 16): the adjacency, its phase,
/// and the playhead of each preview voice the listener hears, in audible time. The outgoing
/// playhead is null once its voice has fallen silent, and the incoming one until its voice is
/// heard and after it ends.</summary>
public sealed record PreviewView(Adjacency Adjacency, PreviewPhase Phase, Playhead? Outgoing, Playhead? Incoming);

/// <summary>Which point a capture is for: a fade-out point, or a fade-in point. During a preview
/// each reads its own voice (build issue 16).</summary>
public enum CaptureSide
{
    FadeOut,
    FadeIn,
}

/// <summary>The adjacencies the editor can open from one row, each as the queue would reach it
/// (build issue 15). <see cref="Next"/> is this → next: the adjacency ⏭ would reach from the row,
/// by the rule <see cref="PlayerView.NextAdjacency"/> follows from the current row.
/// <see cref="Previous"/> is previous → this: the adjacency whose ⏭ reaches the row, so under
/// repeat-all the first row's comes from the last. Both pass over rows marked unreadable.
/// <see cref="Self"/> is this → this, the row into itself, present only under repeat-one, where it
/// fires at the track's natural end. A row that is itself marked keeps all three, since its mark
/// clears when its file opens again. Each is null where the queue has none.</summary>
public sealed record RowAdjacencies(Adjacency? Previous, Adjacency? Next, Adjacency? Self)
{
    /// <summary>A row that is not in the queue has none.</summary>
    public static RowAdjacencies None { get; } = new(null, null, null);
}

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
