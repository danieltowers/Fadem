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
    /// voice's spawn. The incoming track is being opened from when the boundary is scheduled
    /// (<see cref="Prepare"/>).</summary>
    private sealed class BoundarySchedule
    {
        public required MixerVoice OutgoingVoice { get; init; }
        public required QueueEntry Incoming { get; init; }

        /// <summary>The outgoing track's frames, as its source counted them: the source is gone
        /// once the voice has ended.</summary>
        public required long OutgoingFrames { get; init; }

        /// <summary>As the boundary was scheduled: whether the rows after a failed incoming
        /// track are tried again though marked.</summary>
        public required bool RetryMarked { get; init; }
        public List<ScheduledEventHandle> Handles { get; } = new();
        public bool Triggered { get; set; }

        /// <summary>The incoming side as authored. Null for a hard cut, and for a fade-out point
        /// already passed: the incoming track then starts from 0:00 when the outgoing one runs
        /// out.</summary>
        public TransitionSpawn? Spawn { get; set; }

        /// <summary>With a <see cref="Spawn"/>: the timeline frame the outgoing fade begins at,
        /// its length and its curve.</summary>
        public long Trigger { get; set; }
        public long FadeFrames { get; set; }
        public FadeCurveKind Curve { get; set; }

        /// <summary>The fade-out point went by with the incoming track still being read: the
        /// outgoing voice plays on, and the transition begins once the track is there
        /// (<see cref="BeginLateTransition"/>).</summary>
        public bool Late { get; set; }
    }

    // ── Opening tracks: the unreadable are marked and passed over ────────────────────────

    /// <summary>Opens the track at <paramref name="index"/>, or marks its row unreadable and says
    /// why. The incoming track opened for the pending boundary is taken, not opened again; one
    /// still being read is not waited for. A row that opens loses its mark. For a command, which
    /// needs its track now: rendering takes only what is prepared (<see cref="TakePrepared"/>).</summary>
    private ITrackSource? TryOpen(int index, out string error)
    {
        var entry = _queue.Entries[index];
        error = "";
        ITrackSource source;
        if (TakePrepared(entry) is { } prepared)
        {
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

    /// <summary>The track's file opened at the engine rate, or null with why it cannot be read.
    /// Held in memory if no larger than <paramref name="memoryCap"/>: by default it is read from
    /// disk as it plays. Touches nothing the lock guards, so a worker may call it.</summary>
    private ITrackSource? OpenTrackFile(
        Track track, out string error, long memoryCap = 0, CancellationToken cancelled = default)
    {
        error = "";
        ITrackSource source;
        TrackFileOpening?.Invoke(track.FilePath);
        try
        {
            source = NormalizedTrackSource.Wrap(TrackSources.Open(track.FilePath, memoryCap, cancelled), _rate);
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
        var voice = AddTrackVoice(_mixer, source, entry.Track, InAlbumRun(entry), timelineStart, spawn, from);
        _voiceEntry[voice] = entry;
        _queue.SetCurrent(index);
        BecomeCurrent(voice, audibleAt ?? timelineStart);
        ScheduleBoundary(index, voice, timelineStart, voice.SourceStart, retryMarked: true);
        Publish();
    }

    /// <summary>A track's voice on <paramref name="mixer"/>, as every boundary starts one: from its
    /// fade-in point with the authored fade-in, or from <paramref name="from"/> at full volume, at
    /// its ReplayGain, its playhead bounded by its length. The queue's voices and a preview's
    /// (build issue 16) both start here. <paramref name="inAlbumRun"/>: it is heard beside
    /// another track of its album, for <see cref="ReplayGainMode.Smart"/>.</summary>
    private MixerVoice AddTrackVoice(
        TransitionMixer mixer, ITrackSource source, Track track, bool inAlbumRun, long timelineStart, TransitionSpawn? spawn,
        long? from = null)
    {
        var sourceStart = Compat.Clamp(
            from ?? (spawn is null ? 0 : Frames(spawn.FadeInPoint)), 0, Math.Max(source.TotalFrames - 1, 0));
        var fadeInFrames = spawn is null ? 0 : Math.Max(0, Frames(spawn.FadeInDuration));
        var voice = mixer.AddVoice(
            source, track, timelineStart, sourceStart, BaseGain(track, inAlbumRun),
            fadeInFrames, spawn?.Curve ?? FadeCurveKind.EqualPower);
        voice.LengthFrames = track.Length.Frames(_rate) > 0 ? track.Length.Frames(_rate) : source.TotalFrames;
        return voice;
    }

    private double BaseGain(Track track, bool inAlbumRun) =>
        ReplayGain.DbFor(track, _settings.ReplayGainMode, inAlbumRun) is { } db
            ? ReplayGain.DbToMultiplier(ReplayGain.ClampDb(db))
            : 1.0;

    /// <summary>Whether the queue plays the row beside another of its album: the row before it
    /// or after it, with shuffle off, since shuffle plays neither next to it.</summary>
    private bool InAlbumRun(QueueEntry entry)
    {
        var index = _queue.IndexOf(entry.Id);
        if (_settings.Shuffle || index < 0)
        {
            return false;
        }
        var entries = _queue.Entries;
        return (index > 0 && ReplayGain.SameAlbum(entry.Track, entries[index - 1].Track))
            || (index + 1 < entries.Count && ReplayGain.SameAlbum(entry.Track, entries[index + 1].Track));
    }

    // ── Scheduling the boundary after a voice ────────────────────────────────────────────

    /// <summary>Resolves and schedules the boundary after the voice's track: effective
    /// transition, authored fade-out trigger, incoming spawn. The reference pair anchors timeline
    /// and source positions: (voice start, start offset) for a fresh voice, or (now, current
    /// position) after a seek. A voice has exactly one boundary: the previous one is cancelled.
    /// The incoming track starts being opened now, not when the fade begins: if it cannot be,
    /// it is marked and the boundary goes to the next readable row, with that adjacency's own
    /// transition, so a fade into silence is never heard. <paramref name="retryMarked"/> when a
    /// new voice starts: the queue has reached the rows after it again. <paramref name="unheard"/>
    /// is how far decoding is ahead of the listener in the voice: a fade-out point within it is
    /// still ahead of what is heard, so the fade begins where decoding is rather than not at all.</summary>
    private void ScheduleBoundary(
        int index, MixerVoice voice, long referenceTimeline, long referencePosition, bool retryMarked, long unheard = 0)
    {
        _passedOver.Clear(); // a new search for the incoming row
        ScheduleBoundary(
            index, voice, referenceTimeline, referencePosition, retryMarked, _queue.IndexAfter(index, _settings.Repeat),
            unheard);
    }

    /// <summary><see cref="ScheduleBoundary(int, MixerVoice, long, long, bool)"/> with the
    /// incoming row sought from <paramref name="first"/>: the row after the voice's own, or
    /// after one that has just failed to open.</summary>
    private void ScheduleBoundary(
        int index, MixerVoice voice, long referenceTimeline, long referencePosition, bool retryMarked, int? first,
        long unheard = 0)
    {
        var total = voice.Source.TotalFrames;
        var outTrack = _queue.Entries[index].Track;

        long TimelineOf(long sourceFrame) => referenceTimeline + (sourceFrame - referencePosition);

        _pendingBoundary = null;
        if (PrepareFirstReadable(first, retryMarked) is not { } incoming)
        {
            return; // the end of the queue: the track plays to its length, with no fade
        }
        var boundary = new BoundarySchedule
        {
            OutgoingVoice = voice, Incoming = incoming, OutgoingFrames = total, RetryMarked = retryMarked,
        };
        _pendingBoundary = boundary;

        var inTrack = incoming.Track;
        var global = _store?.Get(outTrack.FilePath, inTrack.FilePath);
        var transition = _resolver.Resolve(
            global, OverrideOf(outTrack.FilePath, inTrack.FilePath), TimeSpanOf(total), inTrack.Length.TimeSpan);
        if (transition.IsHardCut || !TryComputeFade(referencePosition, unheard, total, transition, TimelineOf, out var trigger, out var fadeFrames))
        {
            // A hard cut, or an authored fade-out point already passed (e.g. manual seek): the
            // incoming track starts when the outgoing one runs out.
            return;
        }
        boundary.Spawn = new TransitionSpawn(transition.FadeInPoint, transition.FadeInDuration, transition.Curve);
        boundary.Trigger = trigger;
        boundary.FadeFrames = fadeFrames;
        boundary.Curve = transition.Curve;
        boundary.Handles.Add(_mixer.Schedule(trigger, () => BeginTransition(boundary)));
    }

    /// <summary>The boundary's fade-out point is reached: the outgoing voice fades from there and
    /// the incoming track spawns under it. Rendering does not wait for a track still being read:
    /// the boundary is late.</summary>
    private void BeginTransition(BoundarySchedule boundary)
    {
        if (boundary.Triggered)
        {
            return;
        }
        if (TakePrepared(boundary.Incoming) is not { } opened)
        {
            boundary.Late = true;
            return;
        }
        boundary.Triggered = true;
        Crossfade(boundary.OutgoingVoice, boundary.Trigger, boundary.FadeFrames, boundary.Curve, opened, boundary.Spawn!);
    }

    /// <summary>The outgoing voice ended at <paramref name="at"/> with its boundary yet to
    /// trigger: at its end for a hard cut, or before its fade-out point when its audio ended
    /// early, as for a short file (ADR 0010). The incoming track starts there, its side as
    /// authored. With nothing to start, playback finishes once heard.</summary>
    private void StartIncomingAt(BoundarySchedule boundary, long at)
    {
        boundary.Triggered = true;
        if (TakePrepared(boundary.Incoming) is { } opened)
        {
            StartTrack(opened, at, boundary.Spawn);
        }
        else if (_prepared is { Source: null } preparing && preparing.Entry == boundary.Incoming)
        {
            // Still being read, and rendering does not wait: silence until it is there.
            _awaited = new Awaited
            {
                Ended = boundary.OutgoingVoice,
                EndedAt = at,
                OutgoingFrames = boundary.OutgoingFrames,
                RetryMarked = boundary.RetryMarked,
                Incoming = boundary.Incoming,
                Spawn = boundary.Spawn,
            };
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
        long unheard,
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
        var point = Compat.Clamp(Frames(transition.FadeOutPoint), 0, total);
        if (point < referencePosition - unheard)
        {
            return false; // the fade-out point lies behind the listener; do not force it after a seek
        }
        // Decoded past but not yet heard: the fade begins where decoding is, a buffer late at most.
        point = Math.Max(point, referencePosition);
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
        var outPath = _queue.Entries[index].Track.FilePath;
        var transition = _resolver.Resolve(
            _store?.Get(outPath, inTrack.FilePath), OverrideOf(outPath, inTrack.FilePath),
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
        var fadeFrames = Compat.Clamp(Frames(transition.FadeOutDuration), 0, total - decodePosition);
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
        // A voice that ended by its fade-out completing fell silent as authored, not early.
        if (entry is not null && (voice.DecodeError is not null || !voice.FadedOutBy(endTimeline)))
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
            StartIncomingAt(boundary, endTimeline);
            if (_currentVoice is not null)
            {
                _outgoingVoice = null; // the outgoing voice just ended; it was not cut mid-fade
                _handoverAt = endTimeline;
                return;
            }
            if (_awaited is not null)
            {
                // The ended voice stays current and in the view, at its length, as the last
                // track's tail does, until the incoming track is there (StartAwaited).
                _currentVoice = voice;
                if (entry is not null)
                {
                    _voiceEntry[voice] = entry;
                }
                Publish();
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
        _awaited = null;
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
        _awaited = null;
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
        _awaited = null;
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
        _awaited = null;
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
