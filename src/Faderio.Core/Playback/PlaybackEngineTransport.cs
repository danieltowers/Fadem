using Faderio.Core.Settings;

namespace Faderio.Core.Playback;

public sealed partial class PlaybackEngine
{
    // ── Transport ─────────────────────────────────────────────────────────────────

    public void Play()
    {
        ThrowIfDisposed();
        lock (_sync)
        {
            if (_output is null)
            {
                return;
            }
            if (_output.State == PlaybackState.Paused)
            {
                _output.Play();
                return;
            }
            if (_output.State == PlaybackState.Playing && _currentVoice is not null)
            {
                return;
            }
            if (_currentVoice is null || _finishedRaised)
            {
                var restartFromTop = _finishedRaised;
                ResetForRestart();
                if (Queue.Count == 0)
                {
                    return;
                }
                if (restartFromTop)
                {
                    Queue.SetCurrent(0); // replay from the top
                }
                StartTrack(Math.Max(Queue.CurrentIndex, 0), 0, null, null);
            }
            _output.Play();
        }
    }

    public void Pause()
    {
        ThrowIfDisposed();
        lock (_sync)
        {
            _output?.Pause();
        }
    }

    public void Stop()
    {
        ThrowIfDisposed();
        lock (_sync)
        {
            ResetForRestart();
            _output?.Stop();
        }
    }

    /// <summary>Skips to the next track. A skip ends any in-flight transition right away (or,
    /// with the let-finish setting, after the outgoing fade completes), and the next adjacency
    /// begins with its own transition.</summary>
    public void Next()
    {
        ThrowIfDisposed();
        lock (_sync)
        {
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
                return;
            }
            CutCurrentAndAdvance();
        }
    }

    public void Previous()
    {
        ThrowIfDisposed();
        lock (_sync)
        {
            if (_currentVoice is null)
            {
                return;
            }
            if (CurrentPosition > TimeSpan.FromSeconds(3))
            {
                Seek(TimeSpan.Zero);
                return;
            }
            var previous = Queue.PreviousIndex();
            if (previous is null)
            {
                Seek(TimeSpan.Zero);
                return;
            }
            CutCurrentVoice();
            StartTrack(previous.Value, _mixer.Timeline, null, null);
        }
    }

    /// <summary>Seeks within the current track. Seeking during an active transition cancels
    /// that track's fade and snaps to full volume; a seek past the authored fade-out point
    /// means the author's handoff is not forced onto the manual scrub.</summary>
    public void Seek(TimeSpan position)
    {
        ThrowIfDisposed();
        lock (_sync)
        {
            var voice = _currentVoice;
            if (voice is null)
            {
                return;
            }
            var total = voice.Source.TotalFrames;
            var frames = Math.Clamp(Frames(position), 0, Math.Max(total - 1, 0));
            voice.Source.Position = frames;

            // The seeked track snaps to full volume: its fades are cancelled.
            voice.CancelFadeIn();
            voice.CancelFadeOut();

            if (_pendingBoundary is { } boundary && boundary.OutgoingVoice == voice && !boundary.Triggered)
            {
                CancelBoundary(boundary);
                _pendingBoundary = null;
                ScheduleBoundary(Queue.CurrentIndex, voice, _mixer.Timeline, voice.Source.Position);
            }
        }
    }
}
