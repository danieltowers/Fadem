using NAudio.Wave;

using Faderio.Core.Domain;
using Faderio.Core.Settings;

namespace Faderio.Core.Playback;

public sealed partial class PlaybackEngine
{
    private void OnVoiceEnded(MixerVoice voice, long endTimeline)
    {
        voice.Source.Dispose();
        _voiceTrackIndex.Remove(voice);

        if (voice.EndSuppressed)
        {
            return; // removed deliberately (skip/seek/stop)
        }
        if (voice == _outgoingVoice)
        {
            _outgoingVoice = null;
            return; // the current (incoming) voice keeps playing; its boundary is already scheduled
        }
        if (voice != _currentVoice)
        {
            return;
        }
        _currentVoice = null;

        // A boundary scheduled to fire exactly at this voice's end (the non-overlapping
        // fade-in-only shape) must spawn the incoming track now, with its authored fade-in —
        // not as a plain hard-cut advance.
        if (_pendingBoundary is { } boundary && boundary.OutgoingVoice == voice && !boundary.Triggered)
        {
            CancelBoundary(boundary);
            _pendingBoundary = null;
            if (boundary.IncomingIndex >= 0)
            {
                boundary.SpawnAction?.Invoke();
                _outgoingVoice = null; // the outgoing voice just ended; it was not cut mid-fade
                return;
            }
        }

        var next = Queue.IndexAfter(Queue.CurrentIndex, Repeat);
        if (next is null)
        {
            FinishPlayback();
            return;
        }
        StartTrack(next.Value, endTimeline, null, null);
    }

    private void CutCurrentVoice()
    {
        if (_currentVoice is not { } voice)
        {
            return;
        }
        CancelPendingBoundaryFor(voice);
        _currentVoice = null;
        _mixer.RemoveVoice(voice); // EndSuppressed: OnVoiceEnded only cleans up
    }

    private void CutCurrentAndAdvance()
    {
        CutCurrentVoice();
        var next = Queue.IndexAfter(Queue.CurrentIndex, Repeat);
        if (next is null)
        {
            FinishPlayback();
            return;
        }
        StartTrack(next.Value, _mixer.Timeline, null, null);
    }

    /// <summary>Default skip mid-transition: the in-flight transition is abandoned right away
    /// — the outgoing voice is removed, the incoming voice snaps to full volume — and the
    /// next adjacency (already scheduled from the incoming voice's own start) takes over.</summary>
    private void AbandonInFlightTransition()
    {
        if (_outgoingVoice is { } outgoing)
        {
            _outgoingVoice = null;
            _mixer.RemoveVoice(outgoing);
        }
        _currentVoice?.CancelFadeIn();
    }

    /// <summary>Let-finish skip: the outgoing fade completes before advancing (issue 0008).</summary>
    private void ScheduleLetFinishAdvance()
    {
        if (_outgoingVoice is not { } outgoing)
        {
            return;
        }
        var fadeEnd = outgoing.FadeOutStart is { } start ? start + outgoing.FadeOutFrames : _mixer.Timeline;
        _mixer.Schedule(Math.Max(fadeEnd, _mixer.Timeline), () =>
        {
            if (_outgoingVoice == outgoing && _mixer.Voices.Contains(outgoing))
            {
                _outgoingVoice = null;
                _mixer.RemoveVoice(outgoing);
            }
        });
    }

    private void CancelPendingBoundaryFor(MixerVoice voice)
    {
        if (_pendingBoundary is { } boundary && boundary.OutgoingVoice == voice && !boundary.Triggered)
        {
            CancelBoundary(boundary);
            _pendingBoundary = null;
        }
    }

    private void CancelBoundary(BoundarySchedule boundary)
    {
        foreach (var handle in boundary.Handles)
        {
            _mixer.Cancel(handle);
        }
        boundary.Handles.Clear();
    }

    private void FinishPlayback()
    {
        if (_finishedRaised)
        {
            return;
        }
        _finishedRaised = true;
        try
        {
            _output?.Stop();
        }
        catch (Exception e)
        {
            RaiseNotice($"Audio output error: {e.Message}");
        }
        PlaybackFinished?.Invoke(this, EventArgs.Empty);
    }
}
