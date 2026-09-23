using Faderio.Core.Domain;
using Faderio.Core.Settings;

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
            _output.Stop();
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

    /// <summary>Skips to the next track. A skip ends any in-flight transition right away (or,
    /// with the let-finish setting, after the outgoing fade completes), and the next adjacency
    /// begins with its own transition.</summary>
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
                // A transition is in flight between the outgoing voice and the current voice.
                if (_settings.SkipBehaviour == SkipBehaviour.LetOutgoingFadeFinish)
                {
                    ScheduleLetFinishAdvance();
                }
                else
                {
                    AbandonInFlightTransition();
                }
                Publish();
                return;
            }
            CutCurrentAndAdvance();
            Publish();
        });
    }

    public void Previous()
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            if (_attendedVoice is null || _currentVoice is null)
            {
                return;
            }
            var playhead = View.Attended?.Playhead ?? Playhead.Zero;
            if (playhead.TotalSeconds > 3)
            {
                Seek(Playhead.Zero);
                return;
            }
            var previous = _queue.PreviousIndex();
            if (previous is null)
            {
                Seek(Playhead.Zero);
                return;
            }
            CutAllVoices();
            StartTrack(previous.Value, _mixer.Timeline, null, null);
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

            if (_outgoingVoice == voice && _currentVoice is { } incoming && incoming != voice)
            {
                // Cancel the transition: the incoming voice goes, the outgoing one is current again.
                CancelPendingBoundaryFor(incoming);
                _voiceEntry.Remove(incoming);
                _mixer.RemoveVoice(incoming);
                _outgoingVoice = null;
                _currentVoice = voice;
                _handoverAt = null;
                var index = IndexOf(voice);
                if (index >= 0)
                {
                    _queue.SetCurrent(index);
                }
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
