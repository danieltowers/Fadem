using Fadem.Core.Domain;

namespace Fadem.Core.Playback;

public sealed partial class PlaybackEngine
{
    // ── Transport ────────────────────────────────────────────────────────────────────────

    /// <summary>The ▶ intent (interim transport issue 02). <paramref name="selected"/> is the row
    /// the user has singled out, if any. While stopped it plays from its start, as
    /// <see cref="PlayRow"/> does; so does any row other than the attended one. The attended row,
    /// no row, or a row no longer in the queue (a stale selection, not a mistake: no notice)
    /// resumes when paused, and otherwise starts the queue's current row.</summary>
    public void Play(RowId? selected = null)
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            EndPreviewFirst();
            if (selected is { } row && _queue.IndexOf(row) >= 0
                && (TransportNow == Transport.Stopped || row != AttendedRow()))
            {
                PlayRowNow(row);
                return;
            }
            if (TransportNow == Transport.Paused)
            {
                PlayOutput();
                Publish();
                return;
            }
            if (_output.State == Transport.Playing && _currentVoice is not null)
            {
                return;
            }
            if (_currentVoice is null || _finished)
            {
                // The marked row: the end of the queue already put the mark back at the top.
                ResetForRestart();
                if (_queue.Count == 0)
                {
                    return;
                }
                var first = OpenFirstReadable(Math.Max(_queue.CurrentIndex, 0), RowAfter, retryMarked: true);
                if (first is null)
                {
                    Publish(); // nothing readable: playback stays stopped
                    return;
                }
                StartTrack(first.Value, 0, null);
            }
            PlayOutput();
            Publish();
        });
    }

    public void Pause()
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            EndPreviewFirst();
            if (_output.State == Transport.Playing)
            {
                _output.Pause();
                Publish();
            }
        });
    }

    /// <summary>Pauses while playing; otherwise <see cref="Play"/> with the selected row.</summary>
    public void TogglePlayPause(RowId? selected = null)
    {
        ThrowIfDisposed();
        // The queue's transport: during a preview it is held, and Play ends the preview, then plays.
        if (View.Transport == Transport.Playing)
        {
            Pause();
        }
        else
        {
            Play(selected);
        }
    }

    /// <summary>While paused or playing, the row the view marks as current, read after
    /// <see cref="ObserveClock"/> so it is the one the listener hears: the attended voice's until
    /// its handover, then the incoming one's.</summary>
    private RowId? AttendedRow() =>
        (_attendedVoice ?? _currentVoice) is { } voice && _voiceEntry.TryGetValue(voice, out var entry)
            ? entry.Id
            : _queue.Current?.Id;

    /// <summary>Stops and resets: the mixer timeline and the device's emitted count return to a
    /// shared zero, so the next Play starts both clocks from one origin (ADR 0001).</summary>
    public void Stop()
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ReleasePreview();
            ResetForRestart();
            StopOutput();
            Publish();
        });
    }

    /// <summary>Plays the given row from its start — the one call that replaces v2's hand-rolled
    /// stop → set-current → play. A row that is gone is a notice. The row is tried once, marked
    /// or not: if it cannot be read, it is marked with a notice and what was playing carries on
    /// (build issue 10). An explicit choice never jumps to a row you did not pick.</summary>
    public void PlayRow(RowId row)
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            EndPreviewFirst();
            PlayRowNow(row);
        });
    }

    private void PlayRowNow(RowId row)
    {
        var index = _queue.IndexOf(row);
        if (index < 0)
        {
            RaiseNotice($"That row is no longer in the queue ({row}).");
            return;
        }
        if (TryOpen(index, out var error) is not { } source)
        {
            RaiseNotice($"Could not play \"{Path.GetFileName(_queue.Entries[index].Track.FilePath)}\": {error}");
            return;
        }
        if (_output.State == Transport.Stopped)
        {
            ResetForRestart();
        }
        else
        {
            CutAllVoices();
            _finished = false;
        }
        _queue.SetCurrent(index);
        StartTrack(new OpenedTrack(index, source), _mixer.Timeline, null);
        PlayOutput();
        Publish();
    }

    /// <summary>Skips to the next adjacency (build issue 06). Mid-transition it lands on the
    /// incoming track at full volume and the outgoing one drops out; it never passes over the
    /// incoming track. Otherwise it hard-cuts, from 0:00, to the row after the current one (under
    /// repeat-all the wrap to the first; under repeat-one still the next row), the adjacency
    /// <see cref="PlayerView.NextAdjacency"/> publishes, and stops on the last row with repeat
    /// off. Paused stays paused.</summary>
    public void Next()
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            EndPreviewFirst();
            if (_currentVoice is null)
            {
                return;
            }
            if (_outgoingVoice is not null)
            {
                AbandonInFlightTransition(); // a transition is in flight
                Publish();
                return;
            }
            CutCurrentAndAdvance();
            Publish();
        });
    }

    /// <summary>Starts the transition into the adjacency ⏭ would reach, now (build issue 08). Only
    /// the fade-out point moves, to the playhead: the fade-out duration (shortened if it would pass
    /// the track's end), the fade-in point, the fade-in and the curve play as authored. With no
    /// fade-out authored, the outgoing track stops now and the incoming one starts at its fade-in
    /// point. An unauthored adjacency resolves as its natural boundary would: a hard cut from 0:00,
    /// or the default crossfade from now when that is on. It acts exactly when
    /// <see cref="PlayerView.CanTransitionNow"/> says so: not while a transition is in flight, nor
    /// with no next adjacency, nor unless playing.</summary>
    public void TransitionNow()
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            EndPreviewFirst();
            // The guard is the view's own flag, so the button and the command cannot disagree.
            if (!View.CanTransitionNow || _currentVoice is not { } voice)
            {
                return;
            }
            StartTransitionNow(voice);
        });
    }

    /// <summary>How far before its transition begins <see cref="CueTransition"/> lands: as a
    /// preview's pre-roll. Not a setting.</summary>
    private static readonly TimeSpan CuePreRoll = TimeSpan.FromSeconds(5);

    /// <summary>Seeks the playing track to 5 s before its coming transition begins, at the
    /// fade-out point as resolved or, for a hard cut or a transition with no fade-out, the
    /// track's end, and plays on from there: paused, it resumes. The transition then plays as
    /// the queue plays it, into the boundary's next track; a fade-out point already passed is
    /// armed again, as by any seek before it. A point less than 5 s in lands at 0:00. It acts
    /// exactly when <see cref="PlayerView.CanCueTransition"/> says so.</summary>
    public void CueTransition()
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            EndPreviewFirst();
            if (!View.CanCueTransition || _currentVoice is not { } voice
                || _pendingBoundary is not { } boundary || boundary.OutgoingVoice != voice)
            {
                return;
            }
            var index = IndexOf(voice);
            if (index < 0)
            {
                return;
            }
            var total = voice.Source.TotalFrames;
            var outPath = _queue.Entries[index].Track.FilePath;
            var inTrack = boundary.Incoming.Track;
            var transition = _resolver.Resolve(
                _store?.Get(outPath, inTrack.FilePath), OverrideOf(outPath, inTrack.FilePath),
                TimeSpanOf(total), inTrack.Length.TimeSpan);
            var begins = transition.IsHardCut || Frames(transition.FadeOutDuration) <= 0
                ? total
                : Compat.Clamp(Frames(transition.FadeOutPoint), 0, total);
            SeekNow(Playhead.FromFrames(Math.Max(0, begins - Frames(CuePreRoll)), _rate));
            if (TransportNow == Transport.Paused)
            {
                PlayOutput();
                Publish();
            }
        });
    }

    /// <summary>Acts on the attended voice (build issue 06). Past 3 s it restarts the track; so
    /// it does at the head of the queue, unless repeat-all wraps it to the last row. Otherwise it
    /// hard-cuts to the previous readable row from 0:00, never through a reverse adjacency, passing
    /// over rows marked unreadable; with none, it restarts the track. Restarting the outgoing
    /// voice mid-transition cancels the transition, as a seek does.</summary>
    public void Previous()
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            EndPreviewFirst();
            if (_attendedVoice is not { } attended || _currentVoice is null)
            {
                return;
            }
            var index = IndexOf(attended);
            var playhead = View.Attended?.Playhead ?? Playhead.Zero;
            int? Back(int i) => _queue.IndexBefore(i, _settings.Repeat);
            var previous = playhead.TotalSeconds > 3
                ? null
                : OpenFirstReadable(Back(index), Back, retryMarked: false, intoPassing: true);
            if (previous is null && !attended.IsFinished)
            {
                Seek(Playhead.Zero);
                return;
            }
            // The previous row, or the last track's tail, which has no source left to seek.
            var target = previous ?? OpenFirstReadable(index, _ => null, retryMarked: true, intoPassing: true);
            CutAllVoices();
            if (target is null)
            {
                FinishPlayback();
                return;
            }
            StartTrack(target.Value, _mixer.Timeline, null);
            LandWhilePaused();
            Publish();
        });
    }

    /// <summary>Seeks the attended voice. A seek during an in-flight transition cancels it and
    /// returns to plain playback of the attended voice (ADR 0001): scrubbing the outgoing track
    /// while the incoming carried on underneath would be incoherent to listen to. A seek past the
    /// authored fade-out point does not force the author's handoff onto a manual scrub.</summary>
    public void Seek(Playhead target)
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            EndPreviewFirst();
            SeekNow(target);
        });
    }

    /// <summary>Moves the attended voice by <paramref name="offset"/> from the playhead the
    /// listener hears (interim transport issue 05): the ±5 s buttons. Clamped to the track, and
    /// otherwise any seek: landing past a fade-out point does not force the transition. With
    /// nothing attended (stopped) it does nothing and says nothing.</summary>
    public void SeekBy(TimeSpan offset)
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            EndPreviewFirst();
            if (View.Attended is { } attended)
            {
                SeekNow(attended.Playhead.Plus(offset));
            }
        });
    }

    private void SeekNow(Playhead target)
    {
        var voice = _attendedVoice;
        if (voice is null || voice.IsFinished)
        {
            return;
        }

        if (_outgoingVoice == voice)
        {
            ReturnToOutgoing();
        }

        var total = voice.Source.TotalFrames;
        var frames = Compat.Clamp(target.Frames(_rate), 0, Math.Max(total - 1, 0));
        voice.Source.Position = frames;
        voice.NoteSeek(_mixer.Timeline, voice.Source.Position);

        // The seeked track snaps to full volume: its fades are cancelled.
        voice.CancelFadeIn();
        voice.CancelFadeOut();

        if (_pendingBoundary is { } boundary && boundary.OutgoingVoice == voice && !boundary.Triggered)
        {
            CancelBoundary(boundary);
            _pendingBoundary = null;
        }
        var voiceIndex = IndexOf(voice);
        if (voiceIndex >= 0)
        {
            ScheduleBoundary(voiceIndex, voice, _mixer.Timeline, voice.Source.Position, retryMarked: false);
        }
        Publish();
    }
}
