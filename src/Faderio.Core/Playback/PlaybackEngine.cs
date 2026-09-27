using System.Collections.Immutable;
using Faderio.Core.Domain;
using Faderio.Core.Persistence;
using Faderio.Core.Queue;
using Faderio.Core.Settings;
using Faderio.Core.Transitions;

namespace Faderio.Core.Playback;

/// <summary>
/// The transition engine behind a command surface and an immutable <see cref="View"/>
/// (ADR 0003). Commands come from any thread and are serialised inside; the view is a lock-free
/// read; the sink is called after the lock is released. Runs at the output's sample rate
/// (ADR 0008). Every command is total: a stale row is a notice, never an exception. Only
/// <see cref="ObjectDisposedException"/> escapes.
/// </summary>
public sealed partial class PlaybackEngine : IDisposable
{
    private readonly IAudioOutput _output;

    /// <summary>The open library's transition store, or null when there is none: no library, or
    /// one that could not be opened. Authoring is then refused with a notice, and every
    /// adjacency plays as unauthored (ADR 0011).</summary>
    private readonly ITransitionStore? _store;
    private readonly Library? _library;
    private readonly IEngineSink _sink;
    private readonly TransitionResolver _resolver;
    private readonly SettingsStore? _settingsStore;
    private PlayerSettings _settings;
    private readonly Random _rng;
    private readonly object _sync = new();
    private readonly TransitionMixer _mixer;

    /// <summary>The preview's own voices (build issue 16), on the same output: the device reads
    /// this mixer instead of the queue's while a preview runs, so the queue's stands still.</summary>
    private readonly TransitionMixer _previewMixer;
    private readonly int _rate;
    private readonly PlaybackQueue _queue;
    private readonly List<QueueEntry> _originalOrder = new();
    private readonly Dictionary<MixerVoice, QueueEntry> _voiceEntry = new();

    /// <summary>The newest voice: where decoding is. Its boundary is the pending one.</summary>
    private MixerVoice? _currentVoice;

    /// <summary>The voice fading out under <see cref="_currentVoice"/> during an overlap.</summary>
    private MixerVoice? _outgoingVoice;

    /// <summary>The voice the listener is hearing (ADR 0001). Lags <see cref="_currentVoice"/>
    /// until the audible handover at <see cref="_handoverAt"/>.</summary>
    private MixerVoice? _attendedVoice;
    private long? _handoverAt;

    private BoundarySchedule? _pendingBoundary;

    /// <summary>The pending boundary's incoming track, opened when the boundary was scheduled
    /// and taken when it starts (build issue 10).</summary>
    private (QueueEntry Entry, ITrackSource Source)? _prepared;

    /// <summary>Rows whose file could not be opened the last time the queue reached them.</summary>
    private readonly HashSet<RowId> _unreadable = new();
    private bool _finished;

    /// <summary>The output failed on its own and has not been reopened: the next start reopens
    /// it (build issue 11).</summary>
    private bool _outputLost;

    /// <summary>The queue is paused with a track to resume while the device is stopped: after
    /// device loss, or after a preview held it (build issues 11 and 16).</summary>
    private bool _parked;

    /// <summary>The queue timeline frame the device's count starts from. Zero, as ADR 0001 has it,
    /// except after a preview: the device stopped and its count returned to zero, but the queue
    /// resumes from where it was held (build issue 16). Reset with the timeline.</summary>
    private long _queueBase;

    /// <summary>The timeline frame after the last voice's last frame, while the device has yet
    /// to emit it: playback finishes when it has.</summary>
    private long? _finishAt;
    private bool _disposed;
    private long _nextRowId = 1;
    private long _version;
    private long _queueVersion;
    private bool _shuffle;
    private RepeatMode _repeat;

    private volatile bool _haltingOutput;
    private static readonly TimeSpan RenderWaitStep = TimeSpan.FromMilliseconds(1);

    private volatile Snapshot _snapshot;
    private int _depth;
    private bool _viewDirty;
    private readonly List<string> _pendingNotices = new();

    /// <summary>An engine on a store and settings of its own, outside any library: the renderer's,
    /// and the tests'. A null store is no library.</summary>
    public PlaybackEngine(
        IAudioOutput output,
        ITransitionStore? store,
        IEngineSink sink,
        SettingsStore? settings = null,
        Random? rng = null)
        : this(output, store, sink, settings, null, rng)
    {
    }

    /// <summary>An engine on an opened library (ADR 0011): its stores, or none when it could not
    /// be opened. What went wrong opening it is raised as notices.</summary>
    public PlaybackEngine(IAudioOutput output, Library library, IEngineSink sink, Random? rng = null)
        : this(output, library.Transitions, sink, library.Settings, library, rng)
    {
    }

    private PlaybackEngine(
        IAudioOutput output,
        ITransitionStore? store,
        IEngineSink sink,
        SettingsStore? settings,
        Library? library,
        Random? rng)
    {
        _output = output;
        _store = store;
        _library = library;
        _sink = sink;
        _settingsStore = settings;
        _settings = settings?.Settings ?? new PlayerSettings();
        _resolver = new TransitionResolver(_settings);
        _rng = rng ?? new Random();
        _rate = output.SampleRate;
        _mixer = new TransitionMixer(_rate);
        _previewMixer = new TransitionMixer(_rate);
        _clips = new ClipLatch(Frames(ClipGap));
        _queue = new PlaybackQueue(_rng);
        _snapshot = BuildSnapshot();

        _mixer.VoiceEnded += OnVoiceEnded;
        _previewMixer.VoiceEnded += OnPreviewVoiceEnded;
        _mixer.Clipped += OnClipped;
        _previewMixer.Clipped += OnPreviewClipped;
        _output.Stopped += OnOutputStopped;
        _output.Initialise(new EngineFrameSource(this));

        // Nothing holds the lock yet, so the stores' trouble opening is told straight away.
        var notices = library?.Notices ?? (settings?.LoadNotice is { } notice ? [notice] : []);
        foreach (var opening in notices)
        {
            _sink.Notice(opening);
        }
    }

    /// <summary>The player at this instant. Discrete state is whatever was last published; the
    /// playhead and the attended voice are derived from the frames the device has emitted right
    /// now, so polling this on a timer is honest without a publication per block.</summary>
    public PlayerView View => _snapshot.At(_output.FramesEmitted.Count);

    /// <summary>The engine's sample rate: the output device's, fixed at launch.</summary>
    public int SampleRate => _rate;

    public void Dispose()
    {
        List<AddCompletion> unanswered;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            unanswered = EndAddRun(cancelled: true, notify: false);
            ReleasePreview();
            ResetForRestart();
            _output.Stopped -= OnOutputStopped;
            HaltOutput(_output.Dispose);
        }
        Complete(unanswered);
    }

    // ── The lock, the drain and the publication ─────────────────────────────────────────

    /// <summary>Runs <paramref name="body"/> under the engine lock, then delivers whatever it
    /// queued for the sink after the lock is released: no handler ever runs under the lock, and
    /// a re-entrant command from a handler is legal. <c>Changed</c> is coalesced to one delivery
    /// carrying the latest view.</summary>
    private T Run<T>(Func<T> body)
    {
        Monitor.Enter(_sync);
        return RunWithLockHeld(body);
    }

    /// <summary><see cref="Run{T}"/> once the lock is held: runs the body, releases the lock,
    /// then delivers.</summary>
    private T RunWithLockHeld<T>(Func<T> body)
    {
        T result;
        PlayerView? view = null;
        string[]? notices = null;
        try
        {
            _depth++;
            try
            {
                result = body();
            }
            finally
            {
                _depth--;
                if (_depth == 0)
                {
                    if (_viewDirty)
                    {
                        _viewDirty = false;
                        view = View;
                    }
                    if (_pendingNotices.Count > 0)
                    {
                        notices = _pendingNotices.ToArray();
                        _pendingNotices.Clear();
                    }
                }
            }
        }
        finally
        {
            Monitor.Exit(_sync);
        }
        if (notices is not null)
        {
            foreach (var notice in notices)
            {
                _sink.Notice(notice);
            }
        }
        if (view is not null)
        {
            _sink.Changed(view);
        }
        return result;
    }

    private void Run(Action body) => Run(() =>
    {
        body();
        return 0;
    });

    /// <summary>The only way the device is stopped: never call <c>_output.Stop()</c> directly.
    /// Clips the device played are latched first, and those it still held are dropped. Neither
    /// is published here, mid-command: every caller publishes once its state is whole.</summary>
    private void StopOutput()
    {
        _clips.LatchHeard(_output.FramesEmitted.Count);
        HaltOutput(_output.Stop);
        _clips.DeviceStopped();
        RefreshClips();
    }

    /// <summary>Stops or disposes the device under the lock. The device may wait for its render
    /// thread (WASAPI joins it), and that thread may be waiting for this lock on its way into a
    /// read: rendering gives up the wait while this runs, so neither waits on the other
    /// (build issue 19).</summary>
    private void HaltOutput(Action halt)
    {
        _haltingOutput = true;
        try
        {
            halt();
        }
        finally
        {
            _haltingOutput = false;
        }
    }

    /// <summary>Rendering's way into the lock: it waits like any command, but gives up while a
    /// command holding the lock is halting the device.</summary>
    private bool TryEnterToRender()
    {
        while (!Monitor.TryEnter(_sync, RenderWaitStep))
        {
            if (_haltingOutput)
            {
                return false;
            }
        }
        return true;
    }

    private void Publish()
    {
        _version++;
        _snapshot = BuildSnapshot();
        _viewDirty = true;
    }

    private void RaiseNotice(string message) => _pendingNotices.Add(message);

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PlaybackEngine));
        }
    }

    /// <summary>Latches the clips, completes a pending audible handover, or finishes playback, once
    /// the device has emitted past it. Called at the start of every locked section, so the
    /// published state is never behind what the listener hears by more than one call.</summary>
    private void ObserveClock()
    {
        HearClips();
        if (_preview is not null)
        {
            ObservePreview(); // the queue is held: its clock stands still
            return;
        }
        var emitted = _output.FramesEmitted.Count + _queueBase;
        if (_handoverAt is { } handover && emitted >= handover)
        {
            CompleteHandover();
        }
        if (_finishAt is { } end && emitted >= end)
        {
            FinishPlayback();
        }
    }

    private void CompleteHandover()
    {
        var previous = _attendedVoice;
        _attendedVoice = _currentVoice;
        _handoverAt = null;
        if (previous is not null && previous != _currentVoice && previous.IsFinished)
        {
            _voiceEntry.Remove(previous);
        }
        Publish();
    }

    /// <summary>Makes <paramref name="voice"/> the current voice. If another voice is attended,
    /// the listener keeps hearing it until <paramref name="audibleAt"/>, the timeline frame at
    /// which the new voice becomes audible and the old one falls silent.</summary>
    private void BecomeCurrent(MixerVoice voice, long audibleAt)
    {
        _currentVoice = voice;
        if (_attendedVoice is null || !_voiceEntry.ContainsKey(_attendedVoice))
        {
            _attendedVoice = voice;
            _handoverAt = null;
        }
        else if (_attendedVoice != voice)
        {
            _handoverAt = audibleAt;
        }
    }

    /// <summary>The queue's transport: the device's, except that a parked queue with a track to
    /// resume is paused, never half-stopped, while the device is stopped (build issue 11) or
    /// plays a preview (build issue 16).</summary>
    private Transport TransportNow =>
        _preview is not null ? (_currentVoice is null ? Transport.Stopped : Transport.Paused)
        : _parked && _currentVoice is not null && _output.State == Transport.Stopped ? Transport.Paused
        : _output.State;

    private long Frames(TimeSpan t) => EngineTime.Frames(t, _rate);

    private TimeSpan TimeSpanOf(long frames) => EngineTime.TimeSpanOf(frames, _rate);

    // ── The snapshot: what View is composed from ────────────────────────────────────────

    private Snapshot BuildSnapshot()
    {
        var current = _currentVoice is { } cv && _voiceEntry.TryGetValue(cv, out var ce) ? VoiceSnap.Of(cv, ce) : null;
        var attended = _attendedVoice is { } av && av != _currentVoice && _voiceEntry.TryGetValue(av, out var ae)
            ? VoiceSnap.Of(av, ae)
            : null;
        if (_authoredPairs.Count > 2 * _queue.Entries.Count + 1024)
        {
            _authoredPairs.Clear(); // pairs of rows long since moved apart or removed
        }
        var snapshot = new Snapshot(
            _version, _queueVersion, TransportNow, _queue.Entries.ToImmutableArray(),
            _queue.Current?.Id, attended, current, _handoverAt, _outgoingVoice is not null, _finishAt is not null,
            _shuffle, _repeat, Adding, _rate, _unreadable.ToImmutableHashSet(), _settings,
            _queueBase, _preview is null ? null : _mixer.Timeline, _previewOrigin, _preview?.Snap(), _clips.Snap());
        return snapshot with { Onward = snapshot.OnwardOf(IsAuthoredPair), Library = _library?.View };
    }

    /// <summary>One voice's clock frozen for the view: its reference history and its length. The
    /// playhead is a pure function of the frames emitted, so the view can be recomputed lock-free
    /// at any instant.</summary>
    private sealed record VoiceClock(ImmutableArray<(long Timeline, long Source)> Refs, long LengthFrames)
    {
        public static VoiceClock Of(MixerVoice voice) => new(voice.References.ToImmutableArray(), voice.LengthFrames);

        public long PlayheadFrames(long emitted)
        {
            var position = Refs[0].Source;
            for (var i = Refs.Length - 1; i >= 0; i--)
            {
                if (Refs[i].Timeline <= emitted)
                {
                    position = Refs[i].Source + (emitted - Refs[i].Timeline);
                    break;
                }
            }
            return Math.Clamp(position, 0, Math.Max(LengthFrames, 0));
        }
    }

    /// <summary>One queue voice frozen for the view: its row and its clock.</summary>
    private sealed record VoiceSnap(QueueEntry Entry, VoiceClock Clock)
    {
        public static VoiceSnap Of(MixerVoice voice, QueueEntry entry) => new(entry, VoiceClock.Of(voice));

        public VoiceView At(long emitted, QueueRow row, int rate) =>
            new(row, Playhead.FromFrames(Clock.PlayheadFrames(emitted), rate), Entry.Track.Length);
    }

    private sealed record Snapshot(
        long Version,
        long QueueVersion,
        Transport Transport,
        ImmutableArray<QueueEntry> Entries,
        RowId? CurrentRow,
        VoiceSnap? Attended,
        VoiceSnap? Current,
        long? HandoverAt,
        bool Overlap,
        bool Finishing,
        bool Shuffle,
        RepeatMode Repeat,
        AddProgress? Adding,
        int Rate,
        ImmutableHashSet<RowId> Unreadable,
        PlayerSettings Settings,
        long QueueBase,
        long? QueueHeldAt,
        long PreviewOrigin,
        PreviewSnap? Preview,
        ClipSnap Clips)
    {
        /// <summary>Each row's this → next, as <see cref="AdjacenciesOf"/> gives it, and whether
        /// it is authored (interim transport 07). A row with none is absent.</summary>
        public IReadOnlyDictionary<RowId, bool> Onward { get; init; } = new Dictionary<RowId, bool>();

        public LibraryView? Library { get; init; }

        /// <param name="emitted">The device's count. The queue's clock is that from
        /// <see cref="QueueBase"/>; while a preview holds the queue, it stops where the queue's
        /// mixer stopped, once the device has played out what it had of the queue. The preview's
        /// clock starts at <see cref="PreviewOrigin"/>, where the device began playing it.</param>
        public PlayerView At(long emitted)
        {
            var clip = Clips.At(emitted);
            var preview = Preview?.At(emitted - PreviewOrigin, Rate);
            emitted = QueueHeldAt is { } held ? Math.Min(emitted + QueueBase, held) : emitted + QueueBase;
            VoiceSnap? attended = null, incoming = null;
            var heardAlone = false;
            if (Transport != Transport.Stopped)
            {
                var handedOver = HandoverAt is null || emitted >= HandoverAt;
                attended = Attended is not null && !handedOver ? Attended : Current;
                incoming = Overlap && attended == Attended ? Current : null;
                heardAlone = handedOver && !Overlap && !Finishing;
            }
            var currentRow = attended?.Entry.Id ?? CurrentRow;
            var rows = Entries.Select(e => Row(e, e.Id == currentRow)).ToList();
            var next = NextAdjacencyFrom(currentRow);
            return new PlayerView(
                Version, QueueVersion, Transport, rows,
                attended?.At(emitted, Row(attended.Entry, true), Rate),
                incoming?.At(emitted, Row(incoming.Entry, false), Rate),
                Shuffle, Repeat, Adding, next,
                CanTransitionNow: Transport == Transport.Playing && heardAlone && next is not null)
            {
                Settings = Settings,
                Preview = preview,
                Clip = clip,
                Library = Library,
            };
        }

        private QueueRow Row(QueueEntry entry, bool isCurrent) =>
            new(entry.Id, entry.Track, isCurrent, Unreadable.Contains(entry.Id),
                Onward.TryGetValue(entry.Id, out var authored) ? authored : null);

        /// <summary>Builds <see cref="Onward"/>: every row's this → next, told whether
        /// <paramref name="authored"/>.</summary>
        public Dictionary<RowId, bool> OnwardOf(Func<QueueEntry, QueueEntry, bool> authored)
        {
            var onward = new Dictionary<RowId, bool>(Entries.Length);
            for (var i = 0; i < Entries.Length; i++)
            {
                if (FirstReadable(i, After) is { } next)
                {
                    onward[Entries[i].Id] = authored(Entries[i], Entries[next]);
                }
            }
            return onward;
        }

        private Adjacency? NextAdjacencyFrom(RowId? row) =>
            IndexOf(row) is { } i && FirstReadable(i, After) is { } next
                ? new Adjacency(Entries[i].Track, Entries[next].Track)
                : null;

        /// <summary>The editor's adjacencies for a row (build issue 15), by the rules ⏭ and ⏮ use.</summary>
        public RowAdjacencies AdjacenciesOf(RowId row)
        {
            if (IndexOf(row) is not { } i)
            {
                return RowAdjacencies.None;
            }
            var track = Entries[i].Track;
            return new RowAdjacencies(
                FirstReadable(i, Before) is { } previous ? new Adjacency(Entries[previous].Track, track) : null,
                NextAdjacencyFrom(row),
                Repeat == RepeatMode.One ? new Adjacency(track, track) : null);
        }

        private int? After(int index) => PlaybackQueue.IndexSkippedTo(index, Entries.Length, Repeat);

        private int? Before(int index) => PlaybackQueue.IndexBefore(index, Entries.Length, Repeat);

        private int? IndexOf(RowId? row)
        {
            for (var i = 0; i < Entries.Length; i++)
            {
                if (Entries[i].Id == row)
                {
                    return i;
                }
            }
            return null;
        }

        /// <summary>The first row <paramref name="step"/> reaches from <paramref name="index"/>
        /// that is not marked unreadable: a skip passes over the marked rows, and so does this.</summary>
        private int? FirstReadable(int index, Func<int, int?> step)
        {
            foreach (var other in PlaybackQueue.Walk(step(index), step))
            {
                if (!Unreadable.Contains(Entries[other].Id))
                {
                    return other;
                }
            }
            return null;
        }
    }

    // ── The output seam ──────────────────────────────────────────────────────────────────

    /// <summary>What the output pulls. Rendering and control calls share one lock, except that a
    /// render still waiting while the device is being stopped reads nothing. A decode error never
    /// reaches here: it ends only its own voice, where it happened, with one notice (build issue
    /// 10). What still can is an engine fault, surfaced as a notice, never swallowed, and never
    /// thrown into the device's thread; its block renders as silence.</summary>
    private sealed class EngineFrameSource : IFrameSource
    {
        private readonly PlaybackEngine _engine;

        public EngineFrameSource(PlaybackEngine engine) => _engine = engine;

        public int SampleRate => _engine._rate;

        public int Channels => TransitionMixer.Channels;

        public Frames Read(float[] interleaved, int offset, Frames wanted)
        {
            var count = (int)wanted.Count;
            if (_engine._disposed)
            {
                Array.Clear(interleaved, offset, count * Channels);
                return wanted;
            }
            if (!_engine.TryEnterToRender())
            {
                return Playback.Frames.Zero; // the device is being stopped: end of stream
            }
            return _engine.RunWithLockHeld(() =>
            {
                _engine.ObserveClock();
                if (_engine._output.State == Transport.Stopped)
                {
                    // Observing the clock stopped the device (playback finished, or a preview
                    // ended): nothing more is heard, and no mixer moves on.
                    return Playback.Frames.Zero;
                }
                try
                {
                    // A preview's voices play instead of the queue's, which stands still.
                    (_engine._preview is null ? _engine._mixer : _engine._previewMixer).Read(interleaved, offset, count);
                }
                catch (Exception e)
                {
                    Array.Clear(interleaved, offset, count * Channels);
                    _engine.RaiseNotice($"Audio rendering error: {e.Message}");
                }
                return wanted;
            });
        }
    }
}
