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

    public void Remove(RowId row)
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            var index = _queue.IndexOf(row);
            if (index < 0)
            {
                RaiseNotice($"That row is no longer in the queue ({row}).");
                return;
            }
            var entry = _queue.Entries[index];
            var wasSounding = _voiceEntry.ContainsValue(entry);
            _queue.RemoveAt(index);
            _originalOrder.Remove(entry);
            if (wasSounding)
            {
                var playing = _output.State != Transport.Stopped;
                CutAllVoices();
                if (playing && _queue.Count > 0)
                {
                    StartTrack(Math.Min(index, _queue.Count - 1), _mixer.Timeline, null, null);
                }
                else
                {
                    FinishPlayback();
                }
            }
            else
            {
                RescheduleCurrentBoundary();
            }
            _queueVersion++;
            Publish();
        });
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
