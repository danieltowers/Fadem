using Faderio.Core.Domain;

namespace Faderio.Core.Playback;

public sealed partial class PlaybackEngine
{
    // ── Transport ────────────────────────────────────────────────────────────────────────

    public void Play()
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
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
                var restartFromTop = _finished;
                ResetForRestart();
                if (_queue.Count == 0)
                {
                    return;
                }
                if (restartFromTop)
                {
                    _queue.SetCurrent(0); // replay from the top
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
            if (_output.State == Transport.Playing)
            {
                _output.Pause();
                Publish();
            }
        });
    }

    public void TogglePlayPause()
    {
        ThrowIfDisposed();
        if (_output.State == Transport.Playing)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    /// <summary>Stops and resets: the mixer timeline and the device's emitted count return to a
    /// shared zero, so the next Play starts both clocks from one origin (ADR 0001).</summary>
    public void Stop()
    {
        ThrowIfDisposed();
        Run(() =>
        {
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
        });
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
            // The guard is the view's own flag, so the button and the command cannot disagree.
            if (!View.CanTransitionNow || _currentVoice is not { } voice)
            {
                return;
            }
            StartTransitionNow(voice);
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
            if (_attendedVoice is not { } attended || _currentVoice is null)
            {
                return;
            }
            var index = IndexOf(attended);
            var playhead = View.Attended?.Playhead ?? Playhead.Zero;
            int? Back(int i) => _queue.IndexBefore(i, _repeat);
            var previous = playhead.TotalSeconds > 3 ? null : OpenFirstReadable(Back(index), Back, retryMarked: false);
            if (previous is null && !attended.IsFinished)
            {
                Seek(Playhead.Zero);
                return;
            }
            // The previous row, or the last track's tail, which has no source left to seek.
            var target = previous ?? OpenFirstReadable(index, _ => null, retryMarked: true);
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
            var frames = Math.Clamp(target.Frames(_rate), 0, Math.Max(total - 1, 0));
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
        });
    }
}
