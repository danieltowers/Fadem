using Faderio.Core.Domain;
using Faderio.Core.Queue;

namespace Faderio.Core.Playback;

public sealed partial class PlaybackEngine
{
    /// <summary>Authored fade-in to apply when a track is started mid-transition.</summary>
    private sealed record TransitionSpawn(TimeSpan FadeInPoint, TimeSpan FadeInDuration, FadeCurveKind Curve);

    /// <summary>A scheduled boundary: the outgoing voice's fade and the incoming voice's spawn.</summary>
    private sealed class BoundarySchedule
    {
        public required MixerVoice OutgoingVoice { get; init; }
        public required int OutgoingIndex { get; init; }
        public required int IncomingIndex { get; init; } // -1 → end of sequence (fade-out only)
        public long FadeOutTrigger { get; init; }
        public long FadeOutFrames { get; init; }
        public List<ScheduledEventHandle> Handles { get; } = new();
        public bool Triggered { get; set; }

        /// <summary>Performs the boundary's outgoing fade + incoming spawn; used both by the
        /// scheduled trigger event and directly when the outgoing voice ends exactly at the
        /// trigger (the non-overlapping shape).</summary>
        public Action? SpawnAction { get; set; }
        public bool EndOfSequence => IncomingIndex < 0;
    }

    // ── Starting voices ──────────────────────────────────────────────────────────────────

    /// <summary>Opens the track at <paramref name="index"/> as a new voice starting at
    /// <paramref name="timelineStart"/>. <paramref name="audibleAt"/> is when the listener will
    /// attend to it: its own start, unless an outgoing voice fades over it until later.</summary>
    private void StartTrack(int index, long timelineStart, TransitionSpawn? spawn, HashSet<int>? skipSet, long? audibleAt = null)
    {
        var entry = _queue.Entries[index];
        var track = entry.Track;
        ITrackSource source;
        try
        {
            source = NormalizedTrackSource.Wrap(TrackSources.Open(track.FilePath), _rate);
        }
        catch (Exception e)
        {
            RaiseNotice($"Skipped \"{Path.GetFileName(track.FilePath)}\": {e.Message}");
            SkipToNextAfter(index, timelineStart, skipSet);
            return;
        }
        if (source.TotalFrames <= 0)
        {
            source.Dispose();
            RaiseNotice($"Skipped \"{Path.GetFileName(track.FilePath)}\": file contains no audio.");
            SkipToNextAfter(index, timelineStart, skipSet);
            return;
        }

        var sourceStart = spawn is null
            ? 0
            : Math.Clamp(Frames(spawn.FadeInPoint), 0, Math.Max(source.TotalFrames - 1, 0));
        var fadeInFrames = spawn is null ? 0 : Math.Max(0, Frames(spawn.FadeInDuration));

        var voice = _mixer.AddVoice(
            source, timelineStart, sourceStart, BaseGain(track),
            fadeInFrames, spawn?.Curve ?? FadeCurveKind.EqualPower);
        voice.LengthFrames = track.Length.Frames(_rate) > 0 ? track.Length.Frames(_rate) : source.TotalFrames;
        _voiceEntry[voice] = entry;
        _queue.SetCurrent(index);
        BecomeCurrent(voice, audibleAt ?? timelineStart);
        ScheduleBoundary(index, voice, timelineStart, sourceStart);
        Publish();
    }

    private void SkipToNextAfter(int failedIndex, long timelineStart, HashSet<int>? skipSet)
    {
        // One bad file never stops playback: skip with a non-blocking notice.
        skipSet ??= new HashSet<int>();
        if (!skipSet.Add(failedIndex))
        {
            FinishPlayback();
            return;
        }
        var next = _queue.IndexAfter(failedIndex, _repeat);
        if (next is null || skipSet.Contains(next.Value))
        {
            FinishPlayback();
            return;
        }
        StartTrack(next.Value, timelineStart, null, skipSet);
    }

    private double BaseGain(Track track)
    {
        if (!_settings.ReplayGainEnabled || track.ReplayGainDb is not { } db)
        {
            return 1.0;
        }
        return ReplayGain.DbToMultiplier(ReplayGain.ClampDb(db));
    }

    // ── Scheduling the boundary after a voice ────────────────────────────────────────────

    /// <summary>Resolves and schedules the boundary after the voice's track: effective
    /// transition → outgoing fade trigger + incoming spawn, all in mixer-frame positions.</summary>
    private void ScheduleBoundary(int index, MixerVoice voice, long referenceTimeline, long referencePosition)
    {
        var total = voice.Source.TotalFrames;
        var outTrack = _queue.Entries[index].Track;
        var nextIndex = _queue.IndexAfter(index, _repeat);

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

        var inTrack = _queue.Entries[nextIndex.Value].Track;
        var global = _store.Get(outTrack.FilePath, inTrack.FilePath);
        var transition = _resolver.Resolve(global, null, TimeSpanOf(total), inTrack.Length.TimeSpan);
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
            // The listener attends to the outgoing voice until it falls silent (ADR 0001).
            StartTrack(nextIndex.Value, trig, spawn, null, audibleAt: trig + dur);
        }

        boundary.SpawnAction = SpawnIncoming;
        boundary.Handles.Add(_mixer.Schedule(trig, SpawnIncoming));
        _pendingBoundary = boundary;
    }

    private bool TryComputeFade(
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
            // (the non-overlapping shape).
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

    // ── Voice lifecycle ──────────────────────────────────────────────────────────────────

    private void OnVoiceEnded(MixerVoice voice, long endTimeline)
    {
        var delivered = voice.Source.Position; // read before the source is gone
        voice.Source.Dispose();
        var entry = _voiceEntry.GetValueOrDefault(voice);
        if (voice != _attendedVoice)
        {
            _voiceEntry.Remove(voice);
        }

        if (voice.EndSuppressed)
        {
            return; // removed deliberately (skip/seek/stop)
        }
        if (entry is not null)
        {
            NoticeShortfall(delivered, entry);
        }
        if (voice == _outgoingVoice)
        {
            _outgoingVoice = null;
            if (_handoverAt is null || endTimeline < _handoverAt)
            {
                _handoverAt = endTimeline; // it fell silent early; the handover is now
            }
            Publish();
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
                _handoverAt = endTimeline;
                return;
            }
        }

        var next = _queue.IndexAfter(_queue.CurrentIndex, _repeat);
        if (next is null)
        {
            FinishPlayback();
            return;
        }
        StartTrack(next.Value, endTimeline, null, null);
    }

    /// <summary>Audio that ends before the issued length says it should (a truncated file, a
    /// lying header) is one notice; the track ends where the audio ends (ADR 0010).</summary>
    private void NoticeShortfall(long delivered, QueueEntry entry)
    {
        var claimed = entry.Track.Length.Frames(_rate);
        var tolerance = Math.Max(1, _rate / 1000); // one millisecond
        if (claimed - delivered > tolerance)
        {
            var shortfall = TimeSpanOf(claimed - delivered);
            RaiseNotice(
                $"\"{Path.GetFileName(entry.Track.FilePath)}\" ends {shortfall.TotalSeconds:F1} s before its header claims.");
        }
    }

    private void CutAllVoices()
    {
        if (_pendingBoundary is { } boundary)
        {
            CancelBoundary(boundary);
            _pendingBoundary = null;
        }
        foreach (var voice in _mixer.Voices.ToList())
        {
            _mixer.RemoveVoice(voice); // EndSuppressed: OnVoiceEnded only cleans up
        }
        _currentVoice = null;
        _outgoingVoice = null;
    }

    private void CutCurrentAndAdvance()
    {
        var current = _queue.CurrentIndex;
        CutAllVoices();
        var next = _queue.IndexAfter(current, _repeat);
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
            if (_attendedVoice == outgoing)
            {
                _handoverAt = _mixer.Timeline; // it falls silent now
            }
        }
        _currentVoice?.CancelFadeIn();
    }

    /// <summary>Cancels the in-flight transition back to its outgoing voice: the incoming voice
    /// goes, and the outgoing one is attended and current again, at full volume, with no
    /// boundary until the caller schedules one.</summary>
    private void ReturnToOutgoing()
    {
        if (_outgoingVoice is not { } outgoing || _currentVoice is not { } incoming || incoming == outgoing)
        {
            return;
        }
        CancelPendingBoundaryFor(incoming);
        _voiceEntry.Remove(incoming);
        _mixer.RemoveVoice(incoming);
        _outgoingVoice = null;
        _currentVoice = outgoing;
        _attendedVoice = outgoing;
        _handoverAt = null;
        outgoing.CancelFadeOut();
        var index = IndexOf(outgoing);
        if (index >= 0)
        {
            _queue.SetCurrent(index);
        }
    }

    /// <summary>Let-finish skip: the outgoing fade completes before advancing.</summary>
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
        if (_finished)
        {
            return;
        }
        _finished = true;
        _currentVoice = null;
        _outgoingVoice = null;
        _attendedVoice = null;
        _handoverAt = null;
        try
        {
            StopOutput();
        }
        catch (Exception e)
        {
            RaiseNotice($"Audio output error: {e.Message}");
        }
        Publish();
    }

    private void ResetForRestart()
    {
        foreach (var voice in _mixer.Voices.ToList())
        {
            voice.Source.Dispose();
        }
        _voiceEntry.Clear();
        _mixer.Reset();
        _currentVoice = null;
        _outgoingVoice = null;
        _attendedVoice = null;
        _handoverAt = null;
        _pendingBoundary = null;
        _finished = false;
    }
}
