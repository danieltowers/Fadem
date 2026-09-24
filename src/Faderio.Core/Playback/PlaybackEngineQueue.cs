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
                var current = _queue.CurrentIndex;
                _queue.SetEntries(entries);
                _queue.SetCurrent(current);
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
            var removing = new HashSet<QueueEntry>();
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
                    removing.Add(_queue.Entries[index]);
                }
            }
            if (gone.Count > 0)
            {
                RaiseNotice(gone.Count == 1
                    ? $"That row is no longer in the queue ({gone[0]})."
                    : $"{gone.Count} of those rows are no longer in the queue.");
            }
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
            }

            if (!allCut)
            {
                if (_currentVoice is { } voice && IndexOf(voice) is var index and >= 0)
                {
                    _queue.SetCurrent(index);
                }
                RescheduleCurrentBoundary();
            }
            else if (replacement is not null)
            {
                StartTrack(_queue.IndexOf(replacement.Id), _mixer.Timeline, null, null);
                if (_output.State == Transport.Paused && _handoverAt is not null)
                {
                    // A paused device emits nothing, so the audible handover would wait for
                    // Play: the view would sit on a row that is gone. Nothing is heard now.
                    CompleteHandover();
                }
            }
            else
            {
                FinishPlayback();
            }
            _queueVersion++;
            Publish();
        });
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
            _output.Stop();
            _queue.Clear();
            _originalOrder.Clear();
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

    private void RestoreOriginalOrder()
    {
        var current = _queue.Current;
        _queue.SetEntries(_originalOrder);
        if (current is not null)
        {
            var index = _queue.IndexOf(current.Id);
            if (index >= 0)
            {
                _queue.SetCurrent(index);
            }
        }
    }

    private int IndexOf(MixerVoice voice) =>
        _voiceEntry.TryGetValue(voice, out var entry) ? _queue.IndexOf(entry.Id) : -1;

    /// <summary>After the queue changes shape, the pending boundary may point at the wrong
    /// incoming track: cancel it and schedule again from the current voice's position.</summary>
    private void RescheduleCurrentBoundary()
    {
        if (_currentVoice is not { } voice)
        {
            return;
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
            ScheduleBoundary(index, voice, _mixer.Timeline, voice.Source.Position);
        }
    }
}
