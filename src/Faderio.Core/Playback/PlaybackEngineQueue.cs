using Faderio.Core.Domain;
using Faderio.Core.Queue;

namespace Faderio.Core.Playback;

public sealed partial class PlaybackEngine
{
    // ── Queue: the only way rows come to exist or move ──────────────────────────────────

    /// <summary>Test seam: queues tracks the caller built, bypassing the probe and the tags.
    /// Lets a Core test set ReplayGain or a deliberate length without writing tags.</summary>
    internal IReadOnlyList<QueueRow> AddTracks(IEnumerable<Track> tracks)
    {
        ThrowIfDisposed();
        return Run(() =>
        {
            var added = tracks.Select(Append).ToList();
            if (added.Count > 0)
            {
                _queueVersion++;
                RescheduleCurrentBoundary();
                Publish();
            }
            return (IReadOnlyList<QueueRow>)added;
        });
    }

    private QueueRow Append(Track track)
    {
        var entry = new QueueEntry(new RowId(_nextRowId++), track);
        _queue.Add(entry);
        _originalOrder.Add(entry);
        if (_shuffle && _queue.Count > 1)
        {
            // A track added while shuffling lands somewhere among the upcoming ones.
            var upcomingFrom = Math.Max(_queue.CurrentIndex + 1, 0);
            if (upcomingFrom < _queue.Count - 1)
            {
                var entries = _queue.Entries.ToList();
                var target = _rng.Next(upcomingFrom, entries.Count);
                entries.RemoveAt(entries.Count - 1);
                entries.Insert(target, entry);
                _queue.Reorder(entries);
            }
        }
        return new QueueRow(entry.Id, track, _queue.Current == entry && _attendedVoice is null);
    }

    /// <summary>Writes the queue in its visible order (under shuffle, the play order) as a UTF-8
    /// playlist at <paramref name="path"/> (<see cref="M3UPlaylist.Write"/>). The queue itself is
    /// never saved; this makes a new playlist from it. The rows are taken under the lock and the
    /// file is written off it. Total: the outcome is a notice either way.</summary>
    public void SaveAsPlaylist(string path)
    {
        ThrowIfDisposed();
        var tracks = Run(() =>
        {
            ObserveClock();
            return _queue.Entries.Select(e => e.Track).ToList();
        });
        string notice;
        try
        {
            M3UPlaylist.Write(path, tracks);
            var count = tracks.Count == 1 ? "1 track" : $"{tracks.Count} tracks";
            notice = $"Saved {count} to {Path.GetFileName(path)}.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            notice = $"Could not save the playlist: {e.Message}";
        }
        Run(() => RaiseNotice(notice));
    }

    /// <summary>Removes the given rows in one call (build issue 04). Removing what plays means
    /// "not this one", not "stop". When every sounding voice's row goes, playback continues on
    /// the first surviving row after them (under repeat-all, the wrap), from 0:00, as a hard cut,
    /// and paused stays paused; with no such row, playback finishes. Mid-transition, a removed
    /// voice is cut and the other plays on at full volume, as a skip lands. Rows already gone are
    /// one notice, and the rest are still removed.</summary>
    public void Remove(IEnumerable<RowId> rows)
    {
        ThrowIfDisposed();
        var ids = rows.Distinct().ToList();
        Run(() =>
        {
            ObserveClock();
            var removing = EntriesOf(ids);
            if (removing.Count == 0)
            {
                return;
            }

            bool IsRemoved(MixerVoice? voice) =>
                voice is not null && _voiceEntry.TryGetValue(voice, out var entry) && removing.Contains(entry);
            var cutCurrent = IsRemoved(_currentVoice);
            var cutOutgoing = IsRemoved(_outgoingVoice);
            var allCut = cutCurrent && (_outgoingVoice is null || cutOutgoing);
            QueueEntry? replacement = null;
            if (allCut)
            {
                replacement = ReplacementFor(_voiceEntry[_currentVoice!], removing);
                CutAllVoices();
            }
            else if (cutOutgoing)
            {
                AbandonInFlightTransition();
            }
            else if (cutCurrent)
            {
                ReturnToOutgoing();
            }

            foreach (var entry in removing)
            {
                _queue.RemoveAt(_queue.IndexOf(entry.Id));
                _originalOrder.Remove(entry);
                _unreadable.Remove(entry.Id);
            }

            if (!allCut)
            {
                if (_currentVoice is { } voice && IndexOf(voice) is var index and >= 0)
                {
                    _queue.SetCurrent(index);
                }
                RescheduleCurrentBoundary();
            }
            else if (replacement is not null
                && OpenFirstReadable(_queue.IndexOf(replacement.Id), RowAfter, retryMarked: false) is { } next)
            {
                StartTrack(next, _mixer.Timeline, null);
                LandWhilePaused(); // or the view would sit on a row that is gone
            }
            else
            {
                FinishPlayback();
            }
            _queueVersion++;
            Publish();
        });
    }

    /// <summary>Moves the given rows, in their queue order, to just before
    /// <paramref name="before"/>, or to the end when it is null (build issue 05). Rows keep their
    /// identity, so what plays is never interrupted. If the move changes which row comes next,
    /// the pending boundary follows it, unless its transition has started: one in flight plays
    /// out. Under shuffle it changes the shuffled order, which is the visible one; unshuffled it
    /// is also the order that turning shuffle off returns to. Rows already gone are one notice,
    /// and the rest still move; a <paramref name="before"/> that is gone moves nothing.</summary>
    public void Move(IEnumerable<RowId> rows, RowId? before)
    {
        ThrowIfDisposed();
        var ids = rows.Distinct().ToList();
        Run(() =>
        {
            ObserveClock();
            var entries = _queue.Entries.ToList();
            var beforeIndex = before is { } b ? _queue.IndexOf(b) : entries.Count;
            if (beforeIndex < 0)
            {
                RaiseNotice($"That row is no longer in the queue ({before}).");
                return;
            }
            var moving = EntriesOf(ids);
            // Dropped before one of the moving rows, they land before the next row that stays.
            var anchor = entries.Skip(beforeIndex).FirstOrDefault(e => !moving.Contains(e));
            var order = entries.Where(e => !moving.Contains(e)).ToList();
            order.InsertRange(anchor is null ? order.Count : order.IndexOf(anchor), entries.Where(moving.Contains));
            if (order.SequenceEqual(entries))
            {
                return;
            }
            _queue.Reorder(order);
            if (!_shuffle)
            {
                // Unshuffled, the visible order is the original order.
                _originalOrder.Clear();
                _originalOrder.AddRange(order);
            }
            RescheduleCurrentBoundary();
            _queueVersion++;
            Publish();
        });
    }

    /// <summary>The queue's entries for <paramref name="ids"/>. Rows already gone are one
    /// notice.</summary>
    private HashSet<QueueEntry> EntriesOf(IEnumerable<RowId> ids)
    {
        var found = new HashSet<QueueEntry>();
        var gone = new List<RowId>();
        foreach (var id in ids)
        {
            var index = _queue.IndexOf(id);
            if (index < 0)
            {
                gone.Add(id);
            }
            else
            {
                found.Add(_queue.Entries[index]);
            }
        }
        if (gone.Count > 0)
        {
            RaiseNotice(gone.Count == 1
                ? $"That row is no longer in the queue ({gone[0]})."
                : $"{gone.Count} of those rows are no longer in the queue.");
        }
        return found;
    }

    /// <summary>The row that takes the place of <paramref name="playing"/> when it and
    /// <paramref name="removing"/> go: the first surviving row after it, or under repeat-all
    /// the first surviving row of all. Null when playback would end.</summary>
    private QueueEntry? ReplacementFor(QueueEntry playing, HashSet<QueueEntry> removing)
    {
        var entries = _queue.Entries;
        for (var i = _queue.IndexOf(playing.Id) + 1; i < entries.Count; i++)
        {
            if (!removing.Contains(entries[i]))
            {
                return entries[i];
            }
        }
        return _repeat == RepeatMode.All ? entries.FirstOrDefault(e => !removing.Contains(e)) : null;
    }

    public void Clear()
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ResetForRestart();
            if (_preview is null) // a preview's device plays on: rows removed do not stop it
            {
                StopOutput();
            }
            _queue.Clear();
            _originalOrder.Clear();
            _unreadable.Clear();
            _queueVersion++;
            Publish();
        });
    }

    /// <summary>Shuffle's original order is recorded in <see cref="Append"/>, the one place rows
    /// enter, so turning shuffle off can never lose the queue.</summary>
    public void SetShuffle(bool on)
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            if (_shuffle == on)
            {
                return;
            }
            _shuffle = on;
            if (on)
            {
                _queue.ShuffleUpcoming();
            }
            else
            {
                RestoreOriginalOrder();
            }
            RescheduleCurrentBoundary();
            _queueVersion++;
            Publish();
        });
    }

    public void SetRepeat(RepeatMode mode)
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            if (_repeat == mode)
            {
                return;
            }
            _repeat = mode;
            RescheduleCurrentBoundary();
            Publish();
        });
    }

    private void RestoreOriginalOrder() => _queue.Reorder(_originalOrder);

    private int IndexOf(MixerVoice voice) =>
        _voiceEntry.TryGetValue(voice, out var entry) ? _queue.IndexOf(entry.Id) : -1;

    /// <summary>After the queue changes shape, the pending boundary may point at the wrong
    /// incoming track: cancel it and schedule again from the current voice's position.</summary>
    private void RescheduleCurrentBoundary()
    {
        if (_currentVoice is not { } voice || voice.IsFinished)
        {
            return; // nothing playing, or the last track's tail: it has no boundary to move
        }
        if (_pendingBoundary is { } boundary)
        {
            if (boundary.Triggered || boundary.OutgoingVoice != voice)
            {
                return; // in flight, or not this voice's: nothing to move
            }
            CancelBoundary(boundary);
            _pendingBoundary = null;
        }
        var index = IndexOf(voice);
        if (index >= 0)
        {
            ScheduleBoundary(index, voice, _mixer.Timeline, voice.Source.Position, retryMarked: false);
        }
    }
}
