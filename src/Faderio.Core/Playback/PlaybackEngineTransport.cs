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
            if (_output.State == Transport.Paused)
            {
                _output.Play();
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
                StartTrack(Math.Max(_queue.CurrentIndex, 0), 0, null, null);
            }
            _output.Play();
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
    /// stop → set-current → play. A row that is gone is a notice.</summary>
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
            StartTrack(index, _mixer.Timeline, null, null);
            _output.Play();
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

    /// <summary>Acts on the attended voice (build issue 06). Past 3 s it restarts the track; so
    /// it does at the head of the queue, unless repeat-all wraps it to the last row. Otherwise it
    /// hard-cuts to the previous row from 0:00, never through a reverse adjacency. Restarting the
    /// outgoing voice mid-transition cancels the transition, as a seek does.</summary>
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
            var previous = playhead.TotalSeconds > 3 ? null : _queue.IndexBefore(index, _repeat);
            if (previous is null && !attended.IsFinished)
            {
                Seek(Playhead.Zero);
                return;
            }
            // The previous row, or the last track's tail, which has no source left to seek.
            CutAllVoices();
            StartTrack(previous ?? index, _mixer.Timeline, null, null);
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
                ScheduleBoundary(voiceIndex, voice, _mixer.Timeline, voice.Source.Position);
            }
            Publish();
        });
    }
}
