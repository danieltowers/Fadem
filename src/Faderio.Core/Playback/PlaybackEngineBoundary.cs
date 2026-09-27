using Faderio.Core.Domain;
using Faderio.Core.Queue;

namespace Faderio.Core.Playback;

public sealed partial class PlaybackEngine
{
    /// <summary>Authored fade-in to apply when a track is started mid-transition.</summary>
    private sealed record TransitionSpawn(TimeSpan FadeInPoint, TimeSpan FadeInDuration, FadeCurveKind Curve);

    /// <summary>A track opened and ready to become a voice: its row and its source.</summary>
    private readonly record struct OpenedTrack(int Index, ITrackSource Source);

    /// <summary>A scheduled boundary: the outgoing voice's fade, or its end, and the incoming
    /// voice's spawn. The incoming track was opened when the boundary was scheduled.</summary>
    private sealed class BoundarySchedule
    {
        public required MixerVoice OutgoingVoice { get; init; }
        public required QueueEntry Incoming { get; init; }
        public List<ScheduledEventHandle> Handles { get; } = new();
        public bool Triggered { get; set; }

        /// <summary>Performs the boundary's outgoing fade + incoming spawn at the given timeline
        /// frame; used both by the scheduled trigger event and directly when the outgoing voice
        /// ends first: exactly at the trigger (the non-overlapping shape and the hard cut), or
        /// before it, when its audio ended early.</summary>
        public Action<long>? SpawnAt { get; set; }
    }

    // ── Opening tracks: the unreadable are marked and passed over ────────────────────────

    /// <summary>Opens the track at <paramref name="index"/>, or marks its row unreadable and says
    /// why. The incoming track opened for the pending boundary is taken, not opened again. A
    /// row that opens loses its mark.</summary>
    private ITrackSource? TryOpen(int index, out string error)
    {
        var entry = _queue.Entries[index];
        error = "";
        ITrackSource source;
        if (_prepared is { } prepared && prepared.Entry == entry)
        {
            _prepared = null;
            source = prepared.Source;
        }
        else if (OpenTrackFile(entry.Track, out error) is { } opened)
        {
            source = opened;
        }
        else
        {
            SetUnreadable(entry, true);
            return null;
        }
        SetUnreadable(entry, false);
        return source;
    }

    /// <summary>The track's file opened at the engine rate, or null with why it cannot be read.</summary>
    private ITrackSource? OpenTrackFile(Track track, out string error)
    {
        error = "";
        ITrackSource source;
        try
        {
            source = NormalizedTrackSource.Wrap(TrackSources.Open(track.FilePath), _rate);
        }
        catch (Exception e)
        {
            error = e.Message;
            return null;
        }
        if (source.TotalFrames <= 0)
        {
            source.Dispose();
            error = "the file contains no audio.";
            return null;
        }
        return source;
    }

    /// <summary>Opens the first readable row from <paramref name="first"/>, stepping with
    /// <paramref name="step"/> past each row that cannot be opened: it is marked, with one notice,
    /// and its row stays. A row already marked is passed over without being tried, unless
    /// <paramref name="retryMarked"/>: the queue reaching it again tries it again. A passing row
    /// (playlists issue 01) is never stepped into, unless <paramref name="intoPassing"/>: going
    /// back, or landing where the listener was. Null when no row is readable.</summary>
    private OpenedTrack? OpenFirstReadable(int? first, Func<int, int?> step, bool retryMarked, bool intoPassing = false)
    {
        foreach (var i in PlaybackQueue.Walk(first, step))
        {
            var entry = _queue.Entries[i];
            if ((!retryMarked && _unreadable.Contains(entry.Id)) || (!intoPassing && _passing.Contains(entry)))
            {
                continue;
            }
            if (TryOpen(i, out var error) is { } source)
            {
                return new OpenedTrack(i, source);
            }
            RaiseNotice($"Passed over \"{Path.GetFileName(entry.Track.FilePath)}\": {error}");
        }
        return null;
    }

    /// <summary>The first readable row a skip reaches from <paramref name="index"/>, passing over
    /// the rows already marked.</summary>
    private OpenedTrack? OpenSkippedTo(int index) =>
        OpenFirstReadable(RowAfter(index), RowAfter, retryMarked: false);

    /// <summary>The row after <paramref name="index"/>, wrapping under repeat-all: the step
    /// passing over takes, whether the user skipped or the queue advanced on its own.</summary>
    private int? RowAfter(int index) => _queue.IndexSkippedTo(index, _settings.Repeat);

    private void SetUnreadable(QueueEntry entry, bool unreadable)
    {
        if (unreadable ? _unreadable.Add(entry.Id) : _unreadable.Remove(entry.Id))
        {
            _queueVersion++;
            Publish();
        }
    }

    /// <summary>Closes the incoming track opened for a boundary that will not use it.</summary>
    private void DropPrepared()
    {
        _prepared?.Source.Dispose();
        _prepared = null;
    }

    // ── Starting voices ──────────────────────────────────────────────────────────────────

    /// <summary>Makes the opened track a new voice starting at <paramref name="timelineStart"/>.
    /// <paramref name="audibleAt"/> is when the listener will attend to it: its own start, unless
    /// an outgoing voice fades over it until later. <paramref name="from"/> starts it part-way
    /// in, at full volume: where a lost device left the listener.</summary>
    private void StartTrack(
        OpenedTrack opened, long timelineStart, TransitionSpawn? spawn, long? audibleAt = null, long? from = null)
    {
        var (index, source) = opened;
        var entry = _queue.Entries[index];
        var voice = AddTrackVoice(_mixer, source, entry.Track, timelineStart, spawn, from);
        _voiceEntry[voice] = entry;
        _queue.SetCurrent(index);
        BecomeCurrent(voice, audibleAt ?? timelineStart);
        ScheduleBoundary(index, voice, timelineStart, voice.SourceStart, retryMarked: true);
        Publish();
    }

    /// <summary>A track's voice on <paramref name="mixer"/>, as every boundary starts one: from its
    /// fade-in point with the authored fade-in, or from <paramref name="from"/> at full volume, at
    /// its ReplayGain, its playhead bounded by its length. The queue's voices and a preview's
    /// (build issue 16) both start here.</summary>
    private MixerVoice AddTrackVoice(
        TransitionMixer mixer, ITrackSource source, Track track, long timelineStart, TransitionSpawn? spawn,
        long? from = null)
    {
        var sourceStart = Math.Clamp(
            from ?? (spawn is null ? 0 : Frames(spawn.FadeInPoint)), 0, Math.Max(source.TotalFrames - 1, 0));
        var fadeInFrames = spawn is null ? 0 : Math.Max(0, Frames(spawn.FadeInDuration));
        var voice = mixer.AddVoice(
            source, track, timelineStart, sourceStart, BaseGain(track),
            fadeInFrames, spawn?.Curve ?? FadeCurveKind.EqualPower);
        voice.LengthFrames = track.Length.Frames(_rate) > 0 ? track.Length.Frames(_rate) : source.TotalFrames;
        return voice;
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
    /// transition → outgoing fade trigger + incoming spawn, all in mixer-frame positions. The
    /// incoming track is opened now, not when the fade begins: if it cannot be, it is marked and
    /// the boundary goes to the next readable row, with that adjacency's own transition, so a
    /// fade into silence is never heard. <paramref name="retryMarked"/> when a new voice starts:
    /// the queue has reached the rows after it again.</summary>
    private void ScheduleBoundary(
        int index, MixerVoice voice, long referenceTimeline, long referencePosition, bool retryMarked)
    {
        var total = voice.Source.TotalFrames;
        var outTrack = _queue.Entries[index].Track;

        long TimelineOf(long sourceFrame) => referenceTimeline + (sourceFrame - referencePosition);

        _pendingBoundary = null;
        var opened = OpenFirstReadable(_queue.IndexAfter(index, _settings.Repeat), RowAfter, retryMarked);
        DropPrepared();
        if (opened is not { } next)
        {
            return; // the end of the queue: the track plays to its length, with no fade
        }
        var incoming = _queue.Entries[next.Index];
        _prepared = (incoming, next.Source);
        var boundary = new BoundarySchedule { OutgoingVoice = voice, Incoming = incoming };
        _pendingBoundary = boundary;

        var inTrack = incoming.Track;
        var global = _store?.Get(outTrack.FilePath, inTrack.FilePath);
        var transition = _resolver.Resolve(global, null, TimeSpanOf(total), inTrack.Length.TimeSpan);
        if (transition.IsHardCut || !TryComputeFade(referencePosition, total, transition, TimelineOf, out var trig, out var dur))
        {
            // A hard cut, or an authored fade-out point already passed (e.g. manual seek): the
            // incoming track starts when the outgoing one runs out.
            boundary.SpawnAt = at =>
            {
                if (!boundary.Triggered)
                {
                    boundary.Triggered = true;
                    StartIncoming(incoming, at, null);
                }
            };
            return;
        }

        var fadeCurve = transition.Curve;
        var spawn = new TransitionSpawn(transition.FadeInPoint, transition.FadeInDuration, transition.Curve);
        var outgoingVoice = voice;

        void SpawnIncoming(long at)
        {
            if (boundary.Triggered)
            {
                return;
            }
            boundary.Triggered = true;
            if (at < trig)
            {
                // The outgoing audio ended before its fade-out point: the boundary fires where
                // it ended, as for a short file (ADR 0010), with the incoming side as authored.
                StartIncoming(incoming, at, spawn);
                return;
            }
            if (OpenIncoming(incoming) is { } opened)
            {
                Crossfade(outgoingVoice, trig, dur, fadeCurve, opened, spawn);
            }
        }

        boundary.SpawnAt = SpawnIncoming;
        boundary.Handles.Add(_mixer.Schedule(trig, () => SpawnIncoming(trig)));
    }

    /// <summary>The boundary's incoming track, opened when it was scheduled. Should its file have
    /// failed since, the next readable row takes its place. A queue change reschedules the
    /// boundary, so the row is gone only if something forgot to.</summary>
    private OpenedTrack? OpenIncoming(QueueEntry incoming) =>
        _queue.IndexOf(incoming.Id) is var index and >= 0
            ? OpenFirstReadable(index, RowAfter, retryMarked: true)
            : null;

    /// <summary>Starts the boundary's incoming track at <paramref name="at"/>, after the outgoing
    /// voice has ended; with nothing readable to start, playback finishes once heard.</summary>
    private void StartIncoming(QueueEntry incoming, long at, TransitionSpawn? spawn)
    {
        if (OpenIncoming(incoming) is { } opened)
        {
            StartTrack(opened, at, spawn);
        }
    }

    /// <summary>Fades <paramref name="outgoing"/> out from <paramref name="start"/> and spawns the
    /// incoming track there under it.</summary>
    private void Crossfade(
        MixerVoice outgoing, long start, long fadeFrames, FadeCurveKind curve, OpenedTrack incoming, TransitionSpawn spawn)
    {
        outgoing.BeginFadeOut(start, fadeFrames, curve);
        _outgoingVoice = outgoing;
        // The listener attends to the outgoing voice until it falls silent (ADR 0001).
        StartTrack(incoming, start, spawn, audibleAt: start + fadeFrames);
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

    // ── Transition now ───────────────────────────────────────────────────────────────────

    /// <summary>The body of <see cref="TransitionNow"/>, for a current voice heard alone. The
    /// adjacency is resolved afresh, since under repeat-one the scheduled boundary is the loop's.
    /// "Now" is where decoding is: the listener hears the fade begin one device buffer later,
    /// as with every command.</summary>
    private void StartTransitionNow(MixerVoice voice)
    {
        var index = IndexOf(voice);
        if (index < 0 || OpenSkippedTo(index) is not { } next)
        {
            return;
        }
        var total = voice.Source.TotalFrames;
        var decodePosition = voice.Source.Position;
        var inTrack = _queue.Entries[next.Index].Track;
        var transition = _resolver.Resolve(
            _store?.Get(_queue.Entries[index].Track.FilePath, inTrack.FilePath), null,
            TimeSpanOf(total), inTrack.Length.TimeSpan, fadeOutAt: TimeSpanOf(decodePosition));
        if (transition.IsHardCut)
        {
            CutTo(next);
            Publish();
            return;
        }

        CancelPendingBoundaryFor(voice);
        var spawn = new TransitionSpawn(transition.FadeInPoint, transition.FadeInDuration, transition.Curve);
        var now = _mixer.Timeline;
        // The resolver has shortened the fade to the track's end; this only absorbs rounding.
        var fadeFrames = Math.Clamp(Frames(transition.FadeOutDuration), 0, total - decodePosition);
        if (fadeFrames == 0)
        {
            CutAllVoices(); // the non-overlapping shape: the outgoing track stops now
            StartTrack(next, now, spawn);
            return;
        }
        Crossfade(voice, now, fadeFrames, transition.Curve, next, spawn);
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
            NoticeEarlyEnd(delivered, entry.Track, voice.DecodeError);
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

        // The boundary fires where this voice ended: at its trigger (a hard cut, or the
        // non-overlapping fade-in-only shape, which keeps its authored fade-in), or before it
        // when the audio ended early.
        if (_pendingBoundary is { } boundary && boundary.OutgoingVoice == voice && !boundary.Triggered)
        {
            CancelBoundary(boundary);
            _pendingBoundary = null;
            boundary.SpawnAt?.Invoke(endTimeline);
            if (_currentVoice is not null)
            {
                _outgoingVoice = null; // the outgoing voice just ended; it was not cut mid-fade
                _handoverAt = endTimeline;
                return;
            }
        }
        // No boundary, or nothing readable left to start: the end of the queue.
        FinishOnceHeard(voice, entry, endTimeline);
    }

    /// <summary>The last voice has rendered its last frame, but the device still holds it.
    /// Playback finishes once the listener has heard that frame (ADR 0001): the final buffer is
    /// not cut, and <see cref="Transport.Stopped"/> follows the playhead. Until then the voice
    /// stays current and in the view, its playhead running on to its length.</summary>
    private void FinishOnceHeard(MixerVoice voice, QueueEntry? entry, long endTimeline)
    {
        _currentVoice = voice;
        if (entry is not null)
        {
            _voiceEntry[voice] = entry;
        }
        _finishAt = endTimeline;
        Publish();
    }

    /// <summary>Audio that ends before the issued length says it should (a truncated file, a
    /// lying header) is one notice; the track ends where the audio ends (ADR 0010). A decode
    /// error mid-track is the same: one notice, and the track ends where it happened.</summary>
    private void NoticeEarlyEnd(long delivered, Track track, Exception? decodeError)
    {
        if (decodeError is not null)
        {
            RaiseNotice(
                $"\"{Path.GetFileName(track.FilePath)}\" could not be read past {TimeSpanOf(delivered).TotalSeconds:F1} s, so it ends there: {decodeError.Message}");
            return;
        }
        var claimed = track.Length.Frames(_rate);
        var tolerance = Math.Max(1, _rate / 1000); // one millisecond
        if (claimed - delivered > tolerance)
        {
            var shortfall = TimeSpanOf(claimed - delivered);
            RaiseNotice(
                $"\"{Path.GetFileName(track.FilePath)}\" ends {shortfall.TotalSeconds:F1} s before its header claims.");
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
        DropPrepared();
        _currentVoice = null;
        _outgoingVoice = null;
        _finishAt = null;
    }

    /// <summary>⏭ outside a transition: a hard cut to the readable row a skip reaches, from
    /// 0:00.</summary>
    private void CutCurrentAndAdvance()
    {
        if (OpenSkippedTo(_queue.CurrentIndex) is { } next)
        {
            CutTo(next);
            return;
        }
        CutAllVoices();
        FinishPlayback();
    }

    /// <summary>A hard cut to <paramref name="next"/>, from 0:00.</summary>
    private void CutTo(OpenedTrack next)
    {
        CutAllVoices();
        StartTrack(next, _mixer.Timeline, null);
        LandWhilePaused();
    }

    /// <summary>A paused device emits nothing, so after a hard cut the audible handover would
    /// wait for Play and the view would sit on the row that was cut. Nothing is heard until then,
    /// so the listener is on the new row now.</summary>
    private void LandWhilePaused()
    {
        if (TransportNow == Transport.Paused && _handoverAt is not null)
        {
            CompleteHandover();
        }
    }

    /// <summary>⏭ mid-transition: the in-flight transition is abandoned right away — the
    /// outgoing voice is removed, the incoming voice snaps to full volume — and the next
    /// adjacency (already scheduled from the incoming voice's own start) takes over.</summary>
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
        DropPrepared();
        _finishAt = null;
        _currentVoice = null;
        _outgoingVoice = null;
        _attendedVoice = null;
        _handoverAt = null;
        if (_queue.Count > 0)
        {
            _queue.SetCurrent(0); // the mark goes where Play starts: the top (interim transport 03)
        }
        if (_preview is null) // a preview's device plays on: rows removed do not stop it
        {
            StopOutputOrNotice();
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
        DropPrepared();
        _mixer.Reset();
        _currentVoice = null;
        _outgoingVoice = null;
        _attendedVoice = null;
        _handoverAt = null;
        _pendingBoundary = null;
        _finished = false;
        _finishAt = null;
        _parked = false;
        _queueBase = 0;
    }
}
