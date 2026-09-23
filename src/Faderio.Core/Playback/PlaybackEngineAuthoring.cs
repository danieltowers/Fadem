using Faderio.Core.Domain;

namespace Faderio.Core.Playback;

public sealed partial class PlaybackEngine
{
    // ── Authoring ────────────────────────────────────────────────────────────────────────

    /// <summary>The attended voice's playhead as a point: the only sound→point path. Returns the
    /// same value <c>View.Attended.Playhead</c> would have at this instant, unquantised. Null
    /// when nothing is playing.</summary>
    public Point? Capture()
    {
        ThrowIfDisposed();
        return Run(() =>
        {
            ObserveClock();
            var attended = View.Attended;
            return attended is null ? (Point?)null : Point.FromTimeSpan(attended.Playhead.TimeSpan);
        });
    }

    /// <summary>The globally authored transition for the adjacency, or null.</summary>
    public TransitionSpec? Transition(Adjacency adjacency)
    {
        ThrowIfDisposed();
        return Run(() => _store.Get(adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath));
    }

    /// <summary>Upserts the transition and, if the adjacency's boundary is pending in the current
    /// playback, reschedules it: editing a transition during the very playback it governs is the
    /// slice's main authoring loop. A boundary already in flight is left alone.</summary>
    public void Author(Adjacency adjacency, TransitionSpec spec)
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            _store.Upsert(adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath, spec);
            RescheduleIfPending(adjacency);
        });
    }

    public void Unauthor(Adjacency adjacency)
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            _store.Remove(adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath);
            RescheduleIfPending(adjacency);
        });
    }

    private void RescheduleIfPending(Adjacency adjacency)
    {
        if (_currentVoice is not { } voice || !_voiceEntry.TryGetValue(voice, out var entry))
        {
            return;
        }
        if (entry.Track.FilePath != adjacency.Outgoing.FilePath)
        {
            return;
        }
        var index = _queue.IndexOf(entry.Id);
        var nextIndex = index < 0 ? null : _queue.IndexAfter(index, _repeat);
        var incomingPath = nextIndex is { } n ? _queue.Entries[n].Track.FilePath : null;
        if (incomingPath is not null && incomingPath != adjacency.Incoming.FilePath)
        {
            return; // the authored adjacency is not the one coming up
        }
        RescheduleCurrentBoundary();
    }
}
