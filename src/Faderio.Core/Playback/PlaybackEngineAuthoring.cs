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
    /// slice's main authoring loop. A boundary already in flight is left alone. The outcome is a
    /// notice: saved, or not saved and why. A store that cannot be written changes nothing, and
    /// the command stays total (build issue 09).</summary>
    public void Author(Adjacency adjacency, TransitionSpec spec)
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            try
            {
                _store.Upsert(adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath, spec);
            }
            catch (Exception e) when (IsStoreWriteFailure(e))
            {
                RaiseStoreWriteFailure("Not saved", adjacency, e);
                return;
            }
            RaiseNotice($"Saved {adjacency.Name}.");
            RescheduleIfPending(adjacency);
        });
    }

    /// <summary>Removes the authored transition, rescheduling as <see cref="Author"/> does. Says
    /// so when there was one to remove, and says why when the store cannot be written.</summary>
    public void Unauthor(Adjacency adjacency)
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            bool removed;
            try
            {
                removed = _store.Remove(adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath);
            }
            catch (Exception e) when (IsStoreWriteFailure(e))
            {
                RaiseStoreWriteFailure("Not removed", adjacency, e);
                return;
            }
            if (removed)
            {
                RaiseNotice($"Removed {adjacency.Name}.");
                RescheduleIfPending(adjacency);
            }
        });
    }

    /// <summary>What a file write can throw, as <see cref="SaveAsPlaylist"/> catches it.</summary>
    private static bool IsStoreWriteFailure(Exception e) =>
        e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private void RaiseStoreWriteFailure(string outcome, Adjacency adjacency, Exception e) =>
        RaiseNotice($"{outcome}: {adjacency.Name}. The transition store could not be written: {e.Message}");

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
