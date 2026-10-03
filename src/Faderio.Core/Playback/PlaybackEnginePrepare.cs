using Faderio.Core.Queue;

namespace Faderio.Core.Playback;

public sealed partial class PlaybackEngine
{
    // ── Preparing the incoming track: opened in the background (phase 6 issue 10) ─────────
    //
    // Opening a file reads through all of it (ADR 0009), which on a slow disk outlasts the device
    // buffer. So rendering never opens one: the boundary's incoming track is opened by a worker
    // as soon as the boundary is scheduled, and rendering takes it only once it is there.

    /// <summary>Test seam: how the open of a boundary's incoming track is started. A background
    /// thread when null; a test may run it inline, or hold it back and run it when it chooses.</summary>
    internal Action<Action>? StartPrepareWorker { get; set; }

    /// <summary>Test seam: told each track file the engine opens, on the thread that opens it,
    /// before it does.</summary>
    internal Action<string>? TrackFileOpening { get; set; }

    /// <summary>Test seam: the largest file a prepared track holds in memory.</summary>
    internal long PreparedMemoryCap { get; set; } = TrackSources.MemoryCap;

    /// <summary>The pending boundary's incoming track: being opened, or opened and waiting.</summary>
    private sealed class Preparation(QueueEntry entry)
    {
        public QueueEntry Entry { get; } = entry;

        /// <summary>Cancelled when the queue moves on before the open has finished.</summary>
        public CancellationTokenSource Dropped { get; } = new();

        /// <summary>The opened track; null while it is still being read.</summary>
        public ITrackSource? Source { get; set; }

        public bool Failed { get; set; }

        /// <summary><see cref="Prepare"/> has yet to return, so a worker run inline leaves the
        /// outcome to its caller.</summary>
        public bool Starting { get; set; }
    }

    private Preparation? _prepared;

    /// <summary>Starts opening <paramref name="entry"/>'s track in the background, unless it is
    /// already the one prepared. False when the open has already failed, as only a worker run
    /// inline can have: the row is marked, with its notice.</summary>
    private bool Prepare(QueueEntry entry)
    {
        if (_prepared is { } held && held.Entry == entry)
        {
            return true;
        }
        DropPrepared();
        var preparing = new Preparation(entry) { Starting = true };
        _prepared = preparing;
        var track = entry.Track;
        var memoryCap = PreparedMemoryCap;
        var dropped = preparing.Dropped.Token;
        void Work()
        {
            var error = "";
            var source = dropped.IsCancellationRequested ? null : OpenTrackFile(track, out error, memoryCap, dropped);
            Run(() => Prepared(preparing, source, error));
        }
        if (StartPrepareWorker is { } start)
        {
            start(Work);
        }
        else
        {
            PrepareOnAThread(Work, preparing);
        }
        preparing.Starting = false;
        return !preparing.Failed;
    }

    /// <summary>The engine's own worker. An engine fault there is a notice, as one while rendering
    /// is: never thrown into a thread nobody watches, where it would end the host's process.
    /// Nothing is left waiting on the track: playback ends where the outgoing voice does.</summary>
    private void PrepareOnAThread(Action work, Preparation preparing) =>
        new Thread(() =>
        {
            try
            {
                work();
            }
            catch (Exception e)
            {
                Run(() =>
                {
                    RaiseNotice($"Could not prepare the next track: {e.Message}");
                    if (_prepared != preparing)
                    {
                        return;
                    }
                    DropPrepared();
                    if (_awaited is { } awaited)
                    {
                        _awaited = null;
                        FinishOnceHeard(awaited.Ended, _voiceEntry.GetValueOrDefault(awaited.Ended), awaited.EndedAt);
                    }
                });
            }
        })
        { IsBackground = true, Name = "faderio prepare" }.Start();

    /// <summary>The worker's answer, under the lock: the track is opened, or cannot be.</summary>
    private void Prepared(Preparation preparing, ITrackSource? source, string error)
    {
        if (!_disposed && !preparing.Starting)
        {
            ObserveClock(); // a locked section of its own, not its caller's
        }
        if (_disposed || _prepared != preparing)
        {
            source?.Dispose(); // dropped while it was being read: the queue has moved on
            return;
        }
        if (source is null)
        {
            _prepared = null;
            preparing.Failed = true;
            SetUnreadable(preparing.Entry, true);
            RaiseNotice($"Passed over \"{Path.GetFileName(preparing.Entry.Track.FilePath)}\": {error}");
            if (!preparing.Starting)
            {
                PassOver(preparing.Entry);
            }
            return;
        }
        preparing.Source = source;
        SetUnreadable(preparing.Entry, false);
        if (preparing.Starting)
        {
            return;
        }
        if (_awaited is not null)
        {
            StartAwaited();
        }
        else if (_pendingBoundary is { Late: true, Triggered: false } late && late.Incoming == preparing.Entry)
        {
            BeginLateTransition(late);
        }
    }

    /// <summary>The incoming track could not be opened, and has been marked: whatever waits on
    /// it goes to the next readable row after it, the boundary with that adjacency's own
    /// transition, from where the outgoing voice is now.</summary>
    private void PassOver(QueueEntry failed)
    {
        _passedOver.Add(failed);
        var next = _queue.IndexOf(failed.Id) is var at and >= 0 ? RowAfter(at) : null;
        if (_awaited is { } awaited)
        {
            AwaitFirstReadable(awaited, next, awaited.RetryMarked);
        }
        else if (_pendingBoundary is { Triggered: false } boundary && boundary.Incoming == failed
            && boundary.OutgoingVoice is { IsFinished: false } voice && IndexOf(voice) is var index and >= 0)
        {
            CancelBoundary(boundary);
            ScheduleBoundary(index, voice, _mixer.Timeline, voice.Source.Position, boundary.RetryMarked, next);
            Publish();
        }
    }

    /// <summary>Awaits the first row from <paramref name="first"/> that may be readable, and
    /// starts it if it is already opened. With no row left, playback finishes where the outgoing
    /// voice ended.</summary>
    private void AwaitFirstReadable(Awaited awaited, int? first, bool retryMarked)
    {
        if (PrepareFirstReadable(first, retryMarked) is not { } incoming)
        {
            _awaited = null;
            FinishOnceHeard(awaited.Ended, _voiceEntry.GetValueOrDefault(awaited.Ended), awaited.EndedAt);
            return;
        }
        if (incoming != awaited.Incoming)
        {
            awaited.Incoming = incoming;
            awaited.Spawn = SpawnInto(awaited, incoming);
        }
        StartAwaited(); // already opened: by a worker run inline, or kept from before
    }

    /// <summary>The incoming side of the awaited voice's adjacency into <paramref name="incoming"/>,
    /// as authored; none for a hard cut.</summary>
    private TransitionSpawn? SpawnInto(Awaited awaited, QueueEntry incoming)
    {
        if (!_voiceEntry.TryGetValue(awaited.Ended, out var outgoing))
        {
            return null;
        }
        var (outPath, inPath) = (outgoing.Track.FilePath, incoming.Track.FilePath);
        var transition = _resolver.Resolve(
            _store?.Get(outPath, inPath), OverrideOf(outPath, inPath),
            TimeSpanOf(awaited.OutgoingFrames), incoming.Track.Length.TimeSpan);
        return transition.IsHardCut
            ? null
            : new TransitionSpawn(transition.FadeInPoint, transition.FadeInDuration, transition.Curve);
    }

    /// <summary>The outgoing voice has ended with the incoming track still being read: the queue
    /// is silent until it is there. The ended voice stays current meanwhile.</summary>
    private sealed class Awaited
    {
        public required MixerVoice Ended { get; init; }

        /// <summary>The timeline frame the outgoing voice ended at.</summary>
        public required long EndedAt { get; init; }
        public required long OutgoingFrames { get; init; }
        public required bool RetryMarked { get; init; }
        public required QueueEntry Incoming { get; set; }

        /// <summary>The incoming side as authored, as its boundary had it.</summary>
        public TransitionSpawn? Spawn { get; set; }
    }

    private Awaited? _awaited;

    /// <summary>The awaited track is there: it starts now, where decoding is, its side as
    /// authored. One notice says how long the silence was.</summary>
    private void StartAwaited()
    {
        if (_awaited is not { } awaited || TakePrepared(awaited.Incoming) is not { } opened)
        {
            return;
        }
        _awaited = null;
        var now = _mixer.Timeline;
        RaiseNotice(
            $"\"{Path.GetFileName(awaited.Incoming.Track.FilePath)}\" was still being read when its turn came: " +
            $"{TimeSpanOf(now - awaited.EndedAt).TotalSeconds:F1} s of silence came before it.");
        StartTrack(opened, now, awaited.Spawn);
    }

    /// <summary>The incoming track is there after its boundary's fade-out point went by: the
    /// transition begins now, where decoding is, as authored but for the outgoing fade, which
    /// is no longer than what is left of its track. One notice says how late.</summary>
    private void BeginLateTransition(BoundarySchedule boundary)
    {
        var outgoing = boundary.OutgoingVoice;
        if (outgoing.IsFinished)
        {
            return;
        }
        var now = _mixer.Timeline;
        var fadeFrames = Math.Min(boundary.FadeFrames, outgoing.Source.TotalFrames - outgoing.Source.Position);
        if (fadeFrames <= 0 || TakePrepared(boundary.Incoming) is not { } opened)
        {
            return; // nothing is left to fade: the incoming track starts where the outgoing one ends
        }
        boundary.Triggered = true;
        RaiseNotice(
            $"\"{Path.GetFileName(boundary.Incoming.Track.FilePath)}\" was still being read when its transition was due, " +
            $"so the transition began {TimeSpanOf(now - boundary.Trigger).TotalSeconds:F1} s late.");
        var spawn = boundary.Spawn!;
        if (fadeFrames < boundary.FadeFrames)
        {
            // The fade-in shortens with the fade-out, or the outgoing voice would be gone while
            // the incoming one had barely risen.
            var kept = fadeFrames / (double)boundary.FadeFrames;
            spawn = spawn with { FadeInDuration = TimeSpan.FromTicks((long)(spawn.FadeInDuration.Ticks * kept)) };
        }
        Crossfade(outgoing, now, fadeFrames, boundary.Curve, opened, spawn);
    }

    /// <summary>A late boundary whose incoming row has not changed keeps waiting for it, with its
    /// transition resolved afresh: an edit to it meanwhile is heard. False when it now resolves
    /// to a hard cut, which is scheduled as any other.</summary>
    private bool StaysLate(BoundarySchedule boundary)
    {
        if (!_voiceEntry.TryGetValue(boundary.OutgoingVoice, out var outgoing))
        {
            return false;
        }
        var (outPath, inPath) = (outgoing.Track.FilePath, boundary.Incoming.Track.FilePath);
        var transition = _resolver.Resolve(
            _store?.Get(outPath, inPath), OverrideOf(outPath, inPath),
            TimeSpanOf(boundary.OutgoingFrames), boundary.Incoming.Track.Length.TimeSpan);
        if (transition.IsHardCut)
        {
            return false;
        }
        boundary.Spawn = new TransitionSpawn(transition.FadeInPoint, transition.FadeInDuration, transition.Curve);
        boundary.FadeFrames = Math.Max(0, Frames(transition.FadeOutDuration));
        boundary.Curve = transition.Curve;
        return true;
    }

    /// <summary>Prepares the first row from <paramref name="first"/> that may be readable,
    /// stepping as a skip does past each row that cannot be opened, as
    /// <see cref="OpenFirstReadable"/> does, but without waiting for the open: a row still being
    /// read is taken on trust. Null, with nothing prepared, when no row is left.</summary>
    private QueueEntry? PrepareFirstReadable(int? first, bool retryMarked)
    {
        foreach (var entry in Candidates(first, retryMarked))
        {
            if (Prepare(entry))
            {
                return entry;
            }
        }
        DropPrepared();
        return null;
    }

    /// <summary>The rows the queue may step into from <paramref name="first"/>, in the order a
    /// skip reaches them: never a passing row, nor a marked one unless
    /// <paramref name="retryMarked"/>.</summary>
    private IEnumerable<QueueEntry> Candidates(int? first, bool retryMarked) =>
        PlaybackQueue.Walk(first, RowAfter)
            .Select(i => _queue.Entries[i])
            .Where(entry => (retryMarked || !_unreadable.Contains(entry.Id))
                && !_passing.Contains(entry) && !_passedOver.Contains(entry));

    /// <summary>The rows whose open has failed in the background during the search for the
    /// pending boundary's incoming track. Each row is tried once in a search, as one walk of
    /// <see cref="OpenFirstReadable"/> tries it, though the search goes on answer by answer:
    /// under repeat-all, with no file readable, it ends. Emptied when a new search begins.</summary>
    private readonly HashSet<QueueEntry> _passedOver = new();

    /// <summary>The prepared track for <paramref name="entry"/>, if it is opened and its row is
    /// still in the queue. Null while it is still being read: rendering never waits for it.</summary>
    private OpenedTrack? TakePrepared(QueueEntry entry)
    {
        if (_prepared is not { Source: { } source } prepared || prepared.Entry != entry)
        {
            return null;
        }
        var index = _queue.IndexOf(entry.Id);
        if (index < 0)
        {
            return null;
        }
        _prepared = null;
        return new OpenedTrack(index, source);
    }

    /// <summary>Lets go of the prepared track: closed if it is opened, and if it is still being
    /// read, closed when the worker answers.</summary>
    private void DropPrepared()
    {
        if (_prepared is { } prepared)
        {
            prepared.Dropped.Cancel();
            prepared.Source?.Dispose();
            _prepared = null;
        }
    }
}
