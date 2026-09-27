using Faderio.Core.Domain;
using Faderio.Core.Transitions;

namespace Faderio.Core.Playback;

public sealed partial class PlaybackEngine
{
    // ── Preview (build issue 16) ─────────────────────────────────────────────────────────

    /// <summary>How long a preview plays before its transition begins, and after its later fade
    /// completes. Neither is a setting.</summary>
    private static readonly TimeSpan PreviewPreRoll = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PreviewTail = TimeSpan.FromSeconds(5);

    private PreviewRun? _preview;

    /// <summary>The device frame the preview's timeline zero plays at: where the device began
    /// playing it, after what it still had of the queue.</summary>
    private long _previewOrigin;

    /// <summary>A running preview: its two voices on <see cref="_previewMixer"/>, whose timeline
    /// zero plays at <see cref="_previewOrigin"/>, and where it is in its plan.</summary>
    private sealed class PreviewRun
    {
        public required Adjacency Adjacency { get; init; }
        public required MixerVoice Outgoing { get; init; }

        /// <summary>The incoming track, opened before the preview started, until its voice starts.</summary>
        public ITrackSource? IncomingSource { get; set; }
        public MixerVoice? Incoming { get; set; }

        /// <summary>Where the transition begins as planned: the fade-out point, or the outgoing
        /// track's end.</summary>
        public required long Trigger { get; init; }
        public ScheduledEventHandle? TriggerHandle { get; set; }
        public bool Triggered { get; set; }

        /// <summary>Starts the incoming side at a timeline frame: at the trigger, or earlier where
        /// the outgoing audio ended.</summary>
        public Action<long> SpawnAt { get; set; } = _ => { };

        public long? OutgoingEndedAt { get; set; }
        public long? IncomingEndedAt { get; set; }

        /// <summary>When the later fade completes; known once the incoming voice has started.</summary>
        public long? FadesDoneAt { get; set; }

        /// <summary>When the preview ends: <see cref="FadesDoneAt"/> plus the tail.</summary>
        public long? EndAt { get; set; }

        public Track TrackOf(MixerVoice voice) => voice == Outgoing ? Adjacency.Outgoing : Adjacency.Incoming;

        public IEnumerable<MixerVoice> Voices => Incoming is null ? [Outgoing] : [Outgoing, Incoming];

        public PreviewSnap Snap() => new(
            Adjacency, VoiceClock.Of(Outgoing), OutgoingEndedAt,
            Incoming is null ? null : VoiceClock.Of(Incoming), Incoming?.TimelineStart, IncomingEndedAt ?? EndAt,
            Trigger, FadesDoneAt);
    }

    /// <summary>A preview frozen for the view, read at its own clock like the queue's voices.</summary>
    private sealed record PreviewSnap(
        Adjacency Adjacency,
        VoiceClock Outgoing,
        long? OutgoingEnd,
        VoiceClock? Incoming,
        long? IncomingStart,
        long? IncomingEnd,
        long Trigger,
        long? FadesDoneAt)
    {
        public PreviewView At(long emitted, int rate)
        {
            var phase = emitted < (IncomingStart ?? Trigger) ? PreviewPhase.PreRoll
                : FadesDoneAt is { } done && emitted >= done ? PreviewPhase.Tail
                : PreviewPhase.Transition;
            Playhead? outgoing = OutgoingEnd is { } outEnd && emitted >= outEnd
                ? null
                : Playhead.FromFrames(Outgoing.PlayheadFrames(emitted), rate);
            Playhead? incoming = Incoming is { } clock && emitted >= IncomingStart && !(IncomingEnd is { } inEnd && emitted >= inEnd)
                ? Playhead.FromFrames(clock.PlayheadFrames(emitted), rate)
                : null;
            return new PreviewView(Adjacency, phase, outgoing, incoming);
        }
    }

    /// <summary>Plays the editor's draft for <paramref name="adjacency"/> on a separate pair of
    /// preview voices, resolved as the engine resolves it (build issue 16): from 5 s before the
    /// transition begins, at the fade-out point or, for a hard cut, the outgoing track's end, to
    /// 5 s after the later fade completes. The queue is held exactly where it is, mid-transition
    /// too: a playing queue is paused there, and stays paused when the preview ends. A running
    /// preview is restarted with this draft. Nothing reaches the store.
    /// <para>The draft's impossible values are refused as <see cref="Author"/> refuses them, and
    /// returned. An unreadable track is a notice and its rows are marked: the preview does not
    /// start, and the queue is never held.</para>
    /// <para>The draft is the <paramref name="layer"/> the editor writes, and the preview plays what
    /// the open playlist would (playlists issue 04): a global draft under the playlist's stored
    /// override, or a drafted override over the stored global, refused as
    /// <see cref="AuthorOverride"/> refuses it.</para></summary>
    public IReadOnlyList<Refusal> Preview(Adjacency adjacency, TransitionSpec draft, TransitionLayer layer = TransitionLayer.Global)
    {
        ThrowIfDisposed();
        return Run(() =>
        {
            ObserveClock();
            var (outPath, inPath) = (adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath);
            // A draft that saves as nothing plays as saving it would leave the adjacency.
            var storedGlobal = layer == TransitionLayer.Playlist ? _store?.Get(outPath, inPath) : null;
            var (global, playlistOverride) = layer == TransitionLayer.Playlist
                ? (storedGlobal, OverrideToStore(storedGlobal, draft))
                : (draft.IsBlank ? null : draft, OverrideOf(outPath, inPath));
            var refusals = layer == TransitionLayer.Global
                ? Refusals(adjacency, draft)
                : Refusals(adjacency, TransitionResolver.Merge(global, playlistOverride) ?? new TransitionSpec());
            if (refusals.Count > 0)
            {
                RaiseNotice($"Not previewed: {adjacency.Name}. " + string.Join(" ", refusals.Select(r => r.Reason)));
                return (IReadOnlyList<Refusal>)refusals;
            }
            if (OpenForPreview(adjacency.Outgoing) is not { } outgoing)
            {
                return [];
            }
            if (OpenForPreview(adjacency.Incoming) is not { } incoming)
            {
                outgoing.Dispose();
                return [];
            }
            if (_preview is not null)
            {
                // Preview again: the device plays on into the new preview.
                _previewOrigin += _previewMixer.Timeline;
                ReleasePreview();
            }
            else
            {
                HoldQueue();
            }
            StartPreview(adjacency, global, playlistOverride, outgoing, incoming);
            Publish();
            return [];
        });
    }

    /// <summary>Ends the preview. A queue held playing stays paused where it was held.</summary>
    public void StopPreview()
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            EndPreviewFirst();
        });
    }

    /// <summary>Opens a previewed track. One that cannot be read is a notice, and every row
    /// holding it is marked; one that opens clears their marks, as the queue's opening does.</summary>
    private ITrackSource? OpenForPreview(Track track)
    {
        var source = OpenTrackFile(track, out var error);
        MarkRowsOf(track, unreadable: source is null);
        if (source is null)
        {
            RaiseNotice($"Could not preview \"{Path.GetFileName(track.FilePath)}\": {error}");
        }
        return source;
    }

    private void MarkRowsOf(Track track, bool unreadable)
    {
        foreach (var entry in _queue.Entries.Where(e => e.Track.FilePath == track.FilePath).ToList())
        {
            SetUnreadable(entry, unreadable);
        }
    }

    /// <summary>Holds the queue so the device can play the preview. Nothing in the queue moves:
    /// its mixer is simply no longer read. A playing device plays on, so the queue is heard up to
    /// where its mixer had got to, then the preview. A paused device is stopped first: the moment
    /// it still held, at most one device buffer, is dropped rather than played before the
    /// preview, and the queue resumes after it.</summary>
    private void HoldQueue()
    {
        if (_output.State == Transport.Playing)
        {
            _previewOrigin = _mixer.Timeline - _queueBase; // the frames the device has had
            return;
        }
        if (_output.State == Transport.Paused)
        {
            StopOutputOrNotice();
        }
        _queueBase = _mixer.Timeline;
        _previewOrigin = 0;
    }

    /// <summary>Lays out the preview on its own timeline, from zero, with the queue's boundary
    /// pieces: the same resolution, fade computation, voices and fades, so it plays what the
    /// queue would. A test holds its samples equal to a render of the same draft.</summary>
    private void StartPreview(
        Adjacency adjacency, TransitionSpec? global, TransitionSpec? playlistOverride, ITrackSource outSource, ITrackSource inSource)
    {
        var total = outSource.TotalFrames;
        var transition = _resolver.Resolve(global, playlistOverride, TimeSpanOf(total), adjacency.Incoming.Length.TimeSpan);
        // Where the queue would begin the transition, in the outgoing track.
        var begins = !transition.IsHardCut && TryComputeFade(0, total, transition, f => f, out var planned, out _)
            ? planned
            : total;
        var start = Math.Max(0, begins - Frames(PreviewPreRoll));
        long TimelineOf(long sourceFrame) => sourceFrame - start;

        _previewMixer.Reset();
        var outVoice = AddTrackVoice(_previewMixer, outSource, adjacency.Outgoing, 0, spawn: null, from: start);
        var run = new PreviewRun
        {
            Adjacency = adjacency,
            Outgoing = outVoice,
            IncomingSource = inSource,
            Trigger = TimelineOf(begins),
        };
        _preview = run;

        if (transition.IsHardCut || !TryComputeFade(start, total, transition, TimelineOf, out var trigger, out var fadeFrames))
        {
            // A hard cut: the incoming track starts when the outgoing one runs out.
            run.SpawnAt = at =>
            {
                if (!run.Triggered)
                {
                    run.Triggered = true;
                    StartPreviewIncoming(run, at, null);
                }
            };
        }
        else
        {
            var spawn = new TransitionSpawn(transition.FadeInPoint, transition.FadeInDuration, transition.Curve);
            var curve = transition.Curve;
            void SpawnIncoming(long at)
            {
                if (run.Triggered)
                {
                    return;
                }
                run.Triggered = true;
                if (at < trigger)
                {
                    // The outgoing audio ended before its fade-out point: the boundary fires where
                    // it ended, as in the queue (ADR 0010).
                    StartPreviewIncoming(run, at, spawn);
                    return;
                }
                outVoice.BeginFadeOut(trigger, fadeFrames, curve);
                StartPreviewIncoming(run, trigger, spawn);
            }
            run.SpawnAt = SpawnIncoming;
            run.TriggerHandle = _previewMixer.Schedule(trigger, () => SpawnIncoming(trigger));
        }

        if (!StartDevice())
        {
            EndPreview(); // no device to play on: said so, and the queue stays held, paused
        }
    }

    /// <summary>The incoming voice starts; now the preview knows when its later fade completes, and
    /// the voice falls silent at the preview's end, exactly, a tail after that.</summary>
    private void StartPreviewIncoming(PreviewRun run, long at, TransitionSpawn? spawn)
    {
        var voice = AddTrackVoice(_previewMixer, run.IncomingSource!, run.Adjacency.Incoming, at, spawn);
        run.IncomingSource = null;
        run.Incoming = voice;
        var fadeOutDone = run.Outgoing.FadeOutStart is { } fadeOut ? fadeOut + run.Outgoing.FadeOutFrames : at;
        var done = Math.Max(fadeOutDone, at + voice.FadeInFrames);
        run.FadesDoneAt = done;
        run.EndAt = done + Frames(PreviewTail);
        voice.SilenceFrom(run.EndAt.Value);
        Publish();
    }

    /// <summary>A preview voice ended. One that ended by fading out, or at the preview's end,
    /// ended as planned; otherwise its audio ended early or failed, which is told as the queue
    /// tells it, and only that voice ends. An outgoing voice that ends before its transition
    /// fires the transition there, as in the queue.</summary>
    private void OnPreviewVoiceEnded(MixerVoice voice, long endTimeline)
    {
        var delivered = voice.Source.Position; // read before the source is gone
        voice.Source.Dispose();
        if (voice.EndSuppressed || _preview is not { } run)
        {
            return;
        }
        if (voice == run.Outgoing)
        {
            run.OutgoingEndedAt = endTimeline;
        }
        else
        {
            run.IncomingEndedAt = endTimeline;
        }
        if (voice.DecodeError is not null || voice.FadeOutStart is not { } fade || endTimeline < fade + voice.FadeOutFrames)
        {
            NoticeEarlyEnd(delivered, run.TrackOf(voice), voice.DecodeError);
        }
        if (voice == run.Outgoing && !run.Triggered)
        {
            if (run.TriggerHandle is { } handle)
            {
                _previewMixer.Cancel(handle);
            }
            run.SpawnAt(endTimeline);
        }
        Publish();
    }

    /// <summary>Ends the preview once the listener has heard its end.</summary>
    private void ObservePreview()
    {
        if (_preview?.EndAt is { } end && _output.FramesEmitted.Count - _previewOrigin >= end)
        {
            EndPreview();
        }
    }

    /// <summary>Queue buttons end a preview first, then act on the held queue.</summary>
    private void EndPreviewFirst()
    {
        if (_preview is not null)
        {
            EndPreview();
        }
    }

    /// <summary>The preview's voices go and the device stops. The held queue stays as it was held:
    /// paused, or stopped. There is nothing to restore: its clock carries on from where its mixer
    /// stopped, and anything it was waiting to hear, such as a handover, is observed.</summary>
    private void EndPreview()
    {
        ReleasePreview();
        StopOutputOrNotice();
        _queueBase = _mixer.Timeline;
        _parked = _currentVoice is not null;
        ObserveClock();
        Publish();
    }

    /// <summary>Releases the preview's voices and its unstarted incoming track.</summary>
    private void ReleasePreview()
    {
        if (_preview is not { } run)
        {
            return;
        }
        _preview = null;
        foreach (var voice in _previewMixer.Voices.ToList())
        {
            _previewMixer.RemoveVoice(voice); // EndSuppressed: the handler only disposes
        }
        run.IncomingSource?.Dispose();
        _previewMixer.Reset();
    }
}
