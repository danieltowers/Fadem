using System.Collections.Immutable;
using Faderio.Core.Domain;
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
    private readonly ITransitionStore _store;
    private readonly IEngineSink _sink;
    private readonly TransitionResolver _resolver;
    private readonly PlayerSettings _settings;
    private readonly Random _rng;
    private readonly object _sync = new();
    private readonly TransitionMixer _mixer;
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

    public PlaybackEngine(
        IAudioOutput output,
        ITransitionStore store,
        IEngineSink sink,
        PlayerSettings? settings = null,
        Random? rng = null)
    {
        _output = output;
        _store = store;
        _sink = sink;
        _settings = settings ?? new PlayerSettings();
        _resolver = new TransitionResolver(_settings);
        _rng = rng ?? new Random();
        _rate = output.SampleRate;
        _mixer = new TransitionMixer(_rate);
        _queue = new PlaybackQueue(_rng);
        _snapshot = BuildSnapshot();

        _mixer.VoiceEnded += OnVoiceEnded;
        _output.Stopped += OnOutputStopped;
        _output.Initialise(new EngineFrameSource(this));
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
    /// </summary>
    private void StopOutput() => HaltOutput(_output.Stop);

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

    /// <summary>Completes a pending audible handover, or finishes playback, once the device has
    /// emitted past it. Called at the start of every locked section, so the published state is
    /// never behind what the listener hears by more than one call.</summary>
    private void ObserveClock()
    {
        var emitted = _output.FramesEmitted.Count;
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

    private void OnOutputStopped(Exception? error)
    {
        if (error is not null)
        {
            Run(() =>
            {
                RaiseNotice($"Audio output stopped: {error.Message}");
                Publish();
            });
        }
    }

    private long Frames(TimeSpan t) => EngineTime.Frames(t, _rate);

    private TimeSpan TimeSpanOf(long frames) => EngineTime.TimeSpanOf(frames, _rate);

    // ── The snapshot: what View is composed from ────────────────────────────────────────

    private Snapshot BuildSnapshot()
    {
        var current = _currentVoice is { } cv && _voiceEntry.TryGetValue(cv, out var ce) ? VoiceSnap.Of(cv, ce) : null;
        var attended = _attendedVoice is { } av && av != _currentVoice && _voiceEntry.TryGetValue(av, out var ae)
            ? VoiceSnap.Of(av, ae)
            : null;
        return new Snapshot(
            _version, _queueVersion, _output.State, _queue.Entries.ToImmutableArray(),
            _queue.Current?.Id, attended, current, _handoverAt, _outgoingVoice is not null, _finishAt is not null,
            _shuffle, _repeat, Adding, _rate, _unreadable.ToImmutableHashSet());
    }

    /// <summary>One voice frozen for the view: its row, its reference history and its length.
    /// The playhead is a pure function of the frames emitted, so the view can be recomputed
    /// lock-free at any instant.</summary>
    private sealed record VoiceSnap(QueueEntry Entry, ImmutableArray<(long Timeline, long Source)> Refs, long LengthFrames)
    {
        public static VoiceSnap Of(MixerVoice voice, QueueEntry entry) =>
            new(entry, voice.References.ToImmutableArray(), voice.LengthFrames);

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

        public VoiceView At(long emitted, bool isCurrent, int rate, ImmutableHashSet<RowId> unreadable) =>
            new(new QueueRow(Entry.Id, Entry.Track, isCurrent, unreadable.Contains(Entry.Id)),
                Playhead.FromFrames(PlayheadFrames(emitted), rate),
                Entry.Track.Length);
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
        ImmutableHashSet<RowId> Unreadable)
    {
        public PlayerView At(long emitted)
        {
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
            var rows = Entries.Select(e => new QueueRow(e.Id, e.Track, e.Id == currentRow, Unreadable.Contains(e.Id))).ToList();
            var next = NextAdjacencyFrom(currentRow);
            return new PlayerView(
                Version, QueueVersion, Transport, rows,
                attended?.At(emitted, true, Rate, Unreadable),
                incoming?.At(emitted, false, Rate, Unreadable),
                Shuffle, Repeat, Adding, next,
                CanTransitionNow: Transport == Transport.Playing && heardAlone && next is not null);
        }

        private Adjacency? NextAdjacencyFrom(RowId? row)
        {
            for (var i = 0; i < Entries.Length; i++)
            {
                if (Entries[i].Id == row)
                {
                    // A skip passes over the rows marked unreadable, and so does this.
                    int? After(int n) => PlaybackQueue.IndexSkippedTo(n, Entries.Length, Repeat);
                    foreach (var next in PlaybackQueue.Walk(After(i), After))
                    {
                        if (!Unreadable.Contains(Entries[next].Id))
                        {
                            return new Adjacency(Entries[i].Track, Entries[next].Track);
                        }
                    }
                    return null;
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
                try
                {
                    _engine._mixer.Read(interleaved, offset, count);
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
