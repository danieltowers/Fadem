using Faderio.Core.Domain;

namespace Faderio.Core.Playback;

public sealed partial class PlaybackEngine
{
    /// <summary>Resolves and schedules the boundary after the voice's track: effective
    /// transition → outgoing fade trigger + incoming spawn, all in mixer-sample positions.</summary>
    private void ScheduleBoundary(int index, MixerVoice voice, long referenceTimeline, long referencePosition)
    {
        var total = voice.Source.TotalFrames;
        var outTrack = Queue.Tracks[index];
        var nextIndex = Queue.IndexAfter(index, Repeat);

        long TimelineOf(long sourceFrame) => referenceTimeline + (sourceFrame - referencePosition);

        if (nextIndex is null)
        {
            // End of sequence: an authored fade-out is honoured with no incoming track.
            var spec = _store.FindForOutgoing(outTrack.FilePath);
            var effective = _resolver.Resolve(spec, null, TimeSpanOf(total), TimeSpan.Zero);
            _pendingBoundary = null;
            if (effective.IsHardCut)
            {
                return;
            }
            if (!TryComputeFade(referencePosition, total, effective, TimelineOf, out var trigger, out var fadeFrames))
            {
                return;
            }
            voice.FadeOutStart = trigger;
            voice.FadeOutFrames = fadeFrames;
            voice.FadeOutCurve = effective.Curve;
            _pendingBoundary = new BoundarySchedule
            {
                OutgoingVoice = voice,
                OutgoingIndex = index,
                IncomingIndex = -1,
                FadeOutTrigger = trigger,
                FadeOutFrames = fadeFrames,
            };
            return;
        }

        var inTrack = Queue.Tracks[nextIndex.Value];
        var global = _store.Get(outTrack.FilePath, inTrack.FilePath);
        var transition = _resolver.Resolve(global, null, TimeSpanOf(total), inTrack.Duration);
        _pendingBoundary = null;
        if (transition.IsHardCut)
        {
            return; // hard cut: the incoming track starts when the outgoing one runs out
        }
        if (!TryComputeFade(referencePosition, total, transition, TimelineOf, out var trig, out var dur))
        {
            return; // authored fade-out point already passed (e.g. manual seek)
        }

        var boundary = new BoundarySchedule
        {
            OutgoingVoice = voice,
            OutgoingIndex = index,
            IncomingIndex = nextIndex.Value,
            FadeOutTrigger = trig,
            FadeOutFrames = dur,
        };
        var fadeCurve = transition.Curve;
        var spawn = new TransitionSpawn(transition.FadeInPoint, transition.FadeInDuration, transition.Curve);
        var outgoingVoice = voice;

        void SpawnIncoming()
        {
            if (boundary.Triggered)
            {
                return;
            }
            boundary.Triggered = true;
            outgoingVoice.FadeOutStart = trig;
            outgoingVoice.FadeOutFrames = dur;
            outgoingVoice.FadeOutCurve = fadeCurve;
            _outgoingVoice = outgoingVoice;
            StartTrack(nextIndex.Value, trig, spawn, null);
        }

        boundary.SpawnAction = SpawnIncoming;
        boundary.Handles.Add(_mixer.Schedule(trig, SpawnIncoming));
        _pendingBoundary = boundary;
    }

    private static bool TryComputeFade(
        long referencePosition,
        long total,
        EffectiveTransition transition,
        Func<long, long> timelineOf,
        out long trigger,
        out long fadeFrames)
    {
        trigger = 0;
        fadeFrames = 0;
        var duration = Math.Max(0, Frames(transition.FadeOutDuration));
        if (duration == 0)
        {
            // No outgoing fade: the outgoing track completes before the incoming begins
            // (the non-overlapping shape, issue 0006).
            trigger = timelineOf(total);
            return true;
        }
        var point = Math.Clamp(Frames(transition.FadeOutPoint), 0, total);
        if (point < referencePosition)
        {
            return false; // the fade-out point lies behind us; do not force it after a seek
        }
        trigger = timelineOf(point);
        if (point + duration >= total)
        {
            duration = Math.Max(0, timelineOf(total) - trigger); // the fade completes at the track end
        }
        fadeFrames = duration;
        return true;
    }
}
