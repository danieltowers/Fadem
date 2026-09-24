using Faderio.Core.Domain;

namespace Faderio.Core.Playback;

public sealed partial class PlaybackEngine
{
    // ── Authoring ────────────────────────────────────────────────────────────────────────

    /// <summary>The attended voice's playhead as a point: the only sound→point path. Returns the
    /// same value <c>View.Attended.Playhead</c> would have at this instant, unquantised. With
    /// <paramref name="fromEnd"/>, the point is measured from the end of the attended track:
    /// <c>Length − playhead</c>, for a from-end fade-out point (build issue 14). Null when
    /// nothing is playing.</summary>
    public Point? Capture(bool fromEnd = false)
    {
        ThrowIfDisposed();
        return Run(() =>
        {
            ObserveClock();
            if (View.Attended is not { } attended)
            {
                return (Point?)null;
            }
            var point = Point.FromTimeSpan(attended.Playhead.TimeSpan);
            return fromEnd ? MeasuredFromOtherEnd(attended.Row.Track, point) : point;
        });
    }

    /// <summary>The same moment in <paramref name="track"/>, measured from its other end:
    /// ticking or unticking from-end on a fade-out point (build issue 14). On a 3:40 track, 3:12
    /// from the start is 0:28 from the end, and back. A point past the track's length names no
    /// moment in it, so it is returned unchanged for <see cref="Author"/> to refuse.</summary>
    public Point FromOtherEnd(Track track, Point point)
    {
        ThrowIfDisposed();
        return MeasuredFromOtherEnd(track, point);
    }

    private static Point MeasuredFromOtherEnd(Track track, Point point)
    {
        var length = track.Length.TimeSpan;
        return point.TimeSpan > length ? point : Point.FromTimeSpan(length - point.TimeSpan);
    }

    /// <summary>The adjacencies the editor can open from <paramref name="row"/>: this → next,
    /// previous → this and, under repeat-one, this → this (build issue 15). Read from the same
    /// published state as <see cref="View"/>, without the lock. A stale row has none.</summary>
    public RowAdjacencies AdjacenciesOf(RowId row)
    {
        ThrowIfDisposed();
        return _snapshot.AdjacenciesOf(row);
    }

    /// <summary>Whether saving <paramref name="draft"/> would leave the store as it is: it holds
    /// this transition for the adjacency, or holds nothing and the draft is blank, since a blank
    /// save stores nothing. A missing curve is equal-power, as the resolver reads it, and a blank
    /// entry left by an older build is nothing. The editor's test for unsaved edits (build issue
    /// 15).</summary>
    public bool IsAuthored(Adjacency adjacency, TransitionSpec draft)
    {
        ThrowIfDisposed();
        return Run(() =>
        {
            var stored = _store.Get(adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath) is { IsBlank: false } s ? s : null;
            if (stored is null || draft.IsBlank)
            {
                return stored is null && draft.IsBlank;
            }
            return WithCurve(stored) == WithCurve(draft);
        });

        static TransitionSpec WithCurve(TransitionSpec spec) => spec with { Curve = spec.Curve ?? FadeCurveKind.EqualPower };
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
    /// the command stays total (build issue 09).
    /// <para>Impossible values are refused, not stored, and returned with their reasons for the
    /// editor to show beside each field (build issue 14). A duration that only runs past the end
    /// is not refused: the resolver shortens it. A blank spec removes the transition, as
    /// <see cref="Unauthor"/> does, so the store never holds an empty one.</para></summary>
    public IReadOnlyList<Refusal> Author(Adjacency adjacency, TransitionSpec spec)
    {
        ThrowIfDisposed();
        return Run(() =>
        {
            ObserveClock();
            if (spec.IsBlank)
            {
                if (!RemoveTransition(adjacency))
                {
                    RaiseNotice($"Nothing to save: every field of {adjacency.Name} is blank.");
                }
                return (IReadOnlyList<Refusal>)[];
            }
            var refusals = Refusals(adjacency, spec);
            if (refusals.Count > 0)
            {
                RaiseNotice($"Not saved: {adjacency.Name}. " + string.Join(" ", refusals.Select(r => r.Reason)));
                return refusals;
            }
            try
            {
                _store.Upsert(adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath, spec);
            }
            catch (Exception e) when (IsStoreWriteFailure(e))
            {
                RaiseStoreWriteFailure("Not saved", adjacency, e);
                return [];
            }
            RaiseNotice($"Saved {adjacency.Name}.");
            RescheduleIfPending(adjacency);
            return [];
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
            RemoveTransition(adjacency);
        });
    }

    /// <summary><see cref="Unauthor"/>'s body, under the lock. True when a transition was
    /// removed; a failed write is a notice and false.</summary>
    private bool RemoveTransition(Adjacency adjacency)
    {
        bool removed;
        try
        {
            removed = _store.Remove(adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath);
        }
        catch (Exception e) when (IsStoreWriteFailure(e))
        {
            RaiseStoreWriteFailure("Not removed", adjacency, e);
            return false;
        }
        if (removed)
        {
            RaiseNotice($"Removed {adjacency.Name}.");
            RescheduleIfPending(adjacency);
        }
        return removed;
    }

    /// <summary>The values no track can play: a point past its track's length, measured from
    /// either end, and a negative duration. Only the engine knows both lengths.</summary>
    private static List<Refusal> Refusals(Adjacency adjacency, TransitionSpec spec)
    {
        var refusals = new List<Refusal>();
        if (PointRefusal("fade-out point", spec.FadeOutPoint, adjacency.Outgoing) is { } outReason)
        {
            refusals.Add(new Refusal(TransitionField.FadeOutPoint, outReason));
        }
        if (spec.FadeOutDuration < TimeSpan.Zero)
        {
            refusals.Add(new Refusal(TransitionField.FadeOutDuration, "The fade-out duration cannot be negative."));
        }
        if (PointRefusal("fade-in point", spec.FadeInPoint, adjacency.Incoming) is { } inReason)
        {
            refusals.Add(new Refusal(TransitionField.FadeInPoint, inReason));
        }
        if (spec.FadeInDuration < TimeSpan.Zero)
        {
            refusals.Add(new Refusal(TransitionField.FadeInDuration, "The fade-in duration cannot be negative."));
        }
        return refusals;
    }

    private static string? PointRefusal(string name, FadePointSpec? point, Track track)
    {
        if (point is null || point.Position <= track.Length.TimeSpan)
        {
            return null;
        }
        var title = Path.GetFileNameWithoutExtension(track.FilePath);
        return point.FromEnd
            ? $"The {name}, measured from the end, is longer than {title}, which is {track.Length} long."
            : $"The {name} is past the end of {title}, which is {track.Length} long.";
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
