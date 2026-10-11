using Fadem.Core.Domain;
using Fadem.Core.Queue;

namespace Fadem.Core.Playback;

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
                SaveOpenPlaylist();
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
        JoinSectionAt(_originalOrder.Count - 1);
        if (_settings.Shuffle && _queue.Count > 1)
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

    /// <summary>Puts <paramref name="track"/> where <paramref name="job"/>'s drop asked: after its
    /// last placed row while that is still queued, else before its row, never above the passing
    /// rows; or appended when neither is left. Under shuffle the place is in the visible order,
    /// and the order shuffling off returns to has it at the end.</summary>
    private QueueRow Place(Track track, AddJob job)
    {
        var at = job.LastPlaced is { } last && _queue.IndexOf(last.Id) is var after and >= 0 ? after + 1
            : job.Before is { } before && _queue.IndexOf(before) is var index and >= 0 ? index
            : -1;
        if (at < 0)
        {
            return Append(track);
        }
        var order = _queue.Entries.ToList();
        at = Math.Max(at, order.Count(_passing.Contains)); // the passing rows are at the top
        var entry = new QueueEntry(new RowId(_nextRowId++), track);
        order.Insert(at, entry);
        _queue.Add(entry);
        _queue.Reorder(order);
        if (_settings.Shuffle)
        {
            _originalOrder.Add(entry);
        }
        else
        {
            // Unshuffled, the visible order is the original order.
            _originalOrder.Clear();
            _originalOrder.AddRange(order.Where(e => !_passing.Contains(e)));
        }
        JoinSectionAt(_originalOrder.IndexOf(entry));
        job.LastPlaced = entry;
        return new QueueRow(entry.Id, track, _queue.Current == entry && _attendedVoice is null);
    }

    /// <summary>Writes the queue in its visible order (under shuffle, the play order) as a UTF-8
    /// playlist at <paramref name="path"/> (<see cref="M3UPlaylist.Write"/>): the export primitive.
    /// A row still sounding from the playlist open before is in no playlist, so not in this. The
    /// rows are taken under the lock and the file is written off it. Total: the outcome is a
    /// notice either way. Unshuffled, the visible order is the playlist's, so its sections are
    /// written too; under shuffle, whose order runs across them, none are.</summary>
    public void SaveAsPlaylist(string path)
    {
        ThrowIfDisposed();
        var (tracks, sections) = Run(() =>
        {
            ObserveClock();
            var rows = _queue.Entries.Where(e => !_passing.Contains(e)).Select(e => e.Track).ToList();
            return (rows, _settings.Shuffle ? [] : PlaylistSections());
        });
        string notice;
        try
        {
            M3UPlaylist.Write(path, tracks, sections);
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
                _rowSection.Remove(entry.Id);
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
            SaveOpenPlaylist();
        });
    }

    /// <summary>Moves the given rows, in their queue order, to just before
    /// <paramref name="before"/>, or to the end when it is null (build issue 05). Rows keep their
    /// identity, so what plays is never interrupted. If the move changes which row comes next,
    /// the pending boundary follows it, unless its transition has started: one in flight plays
    /// out. Under shuffle it changes the shuffled order, which is the visible one; unshuffled it
    /// is also the order that turning shuffle off returns to. Rows already gone are one notice,
    /// and the rest still move; a <paramref name="before"/> that is gone moves nothing.
    /// Unshuffled, the rows join the section of the row they land before, or at the end the last
    /// row's (phase 6 issue 09); under shuffle they keep their own, since the playlist's order is
    /// not the one changed.</summary>
    public void Move(IEnumerable<RowId> rows, RowId? before) => MoveRows(rows, before, after: null);

    /// <summary>Moves the given rows as <see cref="Move"/> does, to just after
    /// <paramref name="after"/>, and unshuffled into its section: a drop below a section's last
    /// row ends that section, where one above the next section's first row would start that.</summary>
    public void MoveAfter(IEnumerable<RowId> rows, RowId after) => MoveRows(rows, before: null, after);

    private void MoveRows(IEnumerable<RowId> rows, RowId? before, RowId? after)
    {
        ThrowIfDisposed();
        var ids = rows.Distinct().ToList();
        Run(() =>
        {
            ObserveClock();
            var entries = _queue.Entries.ToList();
            var target = after ?? before;
            var targetIndex = target is { } t ? _queue.IndexOf(t) : entries.Count;
            if (targetIndex < 0)
            {
                RaiseNotice($"That row is no longer in the queue ({target}).");
                return;
            }
            var moving = EntriesOf(ids);
            moving.ExceptWith(_passing); // a passing row stays at the top, in no playlist
            // Dropped before one of the moving rows, they land before the next row that stays.
            var anchor = entries.Skip(after is null ? targetIndex : targetIndex + 1).FirstOrDefault(e => !moving.Contains(e));
            var order = entries.Where(e => !moving.Contains(e)).ToList();
            order.InsertRange(anchor is null ? order.Count : order.IndexOf(anchor), entries.Where(moving.Contains));
            order = order.Where(_passing.Contains).Concat(order.Where(e => !_passing.Contains(e))).ToList();
            var joining = after is not null ? SectionOf(entries[targetIndex])
                : anchor is not null ? SectionOf(anchor)
                : _originalOrder.LastOrDefault(e => !moving.Contains(e)) is { } last ? SectionOf(last)
                : null;
            var rejoined = !_settings.Shuffle && moving.Any(e => SectionOf(e) != joining);
            if (order.SequenceEqual(entries) && !rejoined)
            {
                return;
            }
            _queue.Reorder(order);
            if (!_settings.Shuffle)
            {
                // Unshuffled, the visible order is the original order.
                _originalOrder.Clear();
                _originalOrder.AddRange(order.Where(e => !_passing.Contains(e)));
                foreach (var entry in moving)
                {
                    SetSection(entry.Id, joining);
                }
            }
            RescheduleCurrentBoundary();
            _queueVersion++;
            SaveOpenPlaylist();
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
        return _settings.Repeat == RepeatMode.All ? entries.FirstOrDefault(e => !removing.Contains(e)) : null;
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
            EmptyQueue();
            SaveOpenPlaylist();
            Publish();
        });
    }

    /// <summary>Every row goes, with what was kept about them. The voices are the caller's.</summary>
    private void EmptyQueue()
    {
        _queue.Clear();
        _originalOrder.Clear();
        _unreadable.Clear();
        _rowSection.Clear();
        _passing.Clear();
        _queueVersion++;
    }

    /// <summary>Shuffle's original order is recorded in <see cref="Append"/>, the one place rows
    /// enter, so turning shuffle off can never lose the queue. The choice is the open library's,
    /// saved in its settings.json and in force once it is on disk (library issue 04): not saved,
    /// or with no library open, a notice says why and nothing changes.</summary>
    public void SetShuffle(bool on)
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            if (_settings.Shuffle == on || !KeepSettings(_settings with { Shuffle = on }, "shuffle"))
            {
                return;
            }
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

    /// <summary>Saved and put in force as <see cref="SetShuffle"/> is.</summary>
    public void SetRepeat(RepeatMode mode)
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            if (_settings.Repeat == mode || !KeepSettings(_settings with { Repeat = mode }, "repeat"))
            {
                return;
            }
            RescheduleCurrentBoundary();
            Publish();
        });
    }

    /// <summary>The passing rows stay at the top: they were never in the original order.</summary>
    private void RestoreOriginalOrder() =>
        _queue.Reorder(_queue.Entries.Where(_passing.Contains).Concat(_originalOrder).ToList());

    private int IndexOf(MixerVoice voice) =>
        _voiceEntry.TryGetValue(voice, out var entry) ? _queue.IndexOf(entry.Id) : -1;

    /// <summary>After the queue changes shape, the pending boundary may point at the wrong
    /// incoming track: cancel it and schedule again from the current voice's position.</summary>
    private void RescheduleCurrentBoundary()
    {
        if (_awaited is { } awaited)
        {
            // The outgoing voice has ended and waits on its incoming track: that is what moves.
            _passedOver.Clear();
            AwaitFirstReadable(
                awaited, IndexOf(awaited.Ended) is var ended and >= 0 ? _queue.IndexAfter(ended, _settings.Repeat) : null,
                retryMarked: false);
            return;
        }
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
            if (boundary.Late
                && Candidates(_queue.IndexAfter(IndexOf(voice), _settings.Repeat), retryMarked: false).FirstOrDefault() == boundary.Incoming
                && StaysLate(boundary))
            {
                return; // late, and still into the same row: it begins when that track is there
            }
            CancelBoundary(boundary);
            _pendingBoundary = null;
        }
        var index = IndexOf(voice);
        if (index >= 0)
        {
            ScheduleBoundary(index, voice, _mixer.Timeline, voice.Source.Position, retryMarked: false, UnheardFrames);
        }
    }

    /// <summary>How far decoding is ahead of the listener: the frames the device holds. None
    /// while a preview holds the queue, whose clock stands still.</summary>
    private long UnheardFrames =>
        _preview is null ? Math.Max(0, _mixer.Timeline - (_output.FramesEmitted.Count + _queueBase)) : 0;
}
