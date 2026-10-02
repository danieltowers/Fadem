using Faderio.Core.Domain;
using Faderio.Core.Queue;

namespace Faderio.Core.Playback;

public sealed partial class PlaybackEngine
{
    // ── Adding: probes off the lock, rows appended under it one at a time ───────────────

    /// <summary>One call to <see cref="Add"/>: its paths still to resolve, in order, and what
    /// became of the ones resolved so far. A folder or playlist is replaced in place by its files
    /// once listed. Touched only under the engine lock.</summary>
    private sealed class AddJob
    {
        /// <param name="opening">The job loads a playlist being opened: its entries are not named, so
        /// a playlist among them is not followed, and its rows are the file's already, so they are not
        /// written back to it.</param>
        /// <param name="before">The row its files land before, or null for the end.</param>
        public AddJob(IReadOnlyList<string> paths, bool opening = false, RowId? before = null)
        {
            Opening = opening;
            Before = before;
            Remaining = new LinkedList<PendingPath>(paths.Select(p => new PendingPath(p, Named: !opening)));
        }

        public bool Opening { get; }

        public RowId? Before { get; }

        /// <summary>The last row this job placed before <see cref="Before"/>: the next follows it,
        /// so the job's rows stay together in its order.</summary>
        public QueueEntry? LastPlaced { get; set; }

        public LinkedList<PendingPath> Remaining { get; }
        public List<QueueRow> Added { get; } = new();
        public List<Rejection> Rejected { get; } = new();
        public bool Reported { get; set; }
        public TaskCompletionSource<AddReport> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>A path still to resolve. <see cref="Named"/> when the caller passed it to
    /// <see cref="Add"/>, rather than a folder or playlist yielding it: only a named playlist is
    /// opened, so a playlist naming a playlist, or itself, is probed and rejected, never followed.</summary>
    private readonly record struct PendingPath(string Path, bool Named);

    /// <summary>A path taken by the worker, and the run it was taken in.</summary>
    private sealed record AddStep(AddJob Job, PendingPath Pending, long RunId)
    {
        public string Path => Pending.Path;
    }

    /// <summary>A report to hand to its caller once the lock is released.</summary>
    private sealed record AddCompletion(AddJob Job, AddReport Report);

    /// <summary>Every job of the current run, in call order. The run ends when the last path is
    /// resolved, or at once on <see cref="CancelAdd"/>; then the list is empty again.</summary>
    private readonly List<AddJob> _addRun = new();
    private int _addResolved;
    private int _addTotal;

    /// <summary>Bumped when a run is cut short, so a probe that was in flight for it is dropped
    /// when it returns instead of appending a row nobody is waiting for.</summary>
    private long _addRunId;
    private bool _addWorkerAlive;

    /// <summary>Test seam: how one path becomes a track or a rejection. The probe and the tag read
    /// (ADR 0007, ADR 0010) by default. Always called off the engine lock.</summary>
    internal Func<string, (Track? Track, Rejection? Rejection)> ResolvePath { get; set; } = ResolveFromDisk;

    /// <summary>Test seam: how a folder becomes the files it adds, in order.
    /// <see cref="FolderContents.List"/> by default. Always called off the engine lock.</summary>
    internal Func<string, IReadOnlyList<string>> ListFolder { get; set; } = FolderContents.List;

    /// <summary>Test seam: how the add worker is started. A background thread by default; a test
    /// may run it inline, which makes <see cref="Add"/> synchronous but changes nothing else.</summary>
    internal Action<Action> StartAddWorker { get; set; } = work =>
        new Thread(() => work()) { IsBackground = true, Name = "faderio add" }.Start();

    /// <summary>
    /// Queues files at the end of the queue, or before the row <paramref name="before"/> names (a
    /// drop on the queue, phase 6 issue 03), in the given order, and returns at once. A folder
    /// stands for every promised file under it (<see cref="FolderContents"/>), and an
    /// <c>.m3u</c> or <c>.m3u8</c> for its entries (<see cref="M3UPlaylist.Read"/>), a missing one
    /// rejected like any file; each is listed when the add reaches it and counted as one path until
    /// then. The shell never lists a directory or reads a playlist. Each file
    /// is probed and its tags read off the engine lock and off the caller's thread; the lock is
    /// taken only to append its row. <see cref="PlayerView.Adding"/> counts the files resolved.
    /// A second add waits behind the first and shares its progress. When the run ends, one notice
    /// names every rejection with its reason (ADR 0007); a run that refused nothing is silent,
    /// since its rows are the news. Adding never starts playback. The task completes with this
    /// call's own report once its last file is in, after the sink has been told; the run's notice
    /// comes with the last add of the run.
    /// <para>Before a row, the call's rows land together in its order, as <see cref="Move"/>
    /// places rows: in the visible order under shuffle, and at the end of the order shuffling off
    /// returns to; never above a row still passing from the playlist open before. If that row is
    /// gone before the first lands, they go to the end; once one has landed, the rest follow it.
    /// No notice either way: where the rows went is plain to see.</para>
    /// </summary>
    public Task<AddReport> Add(IEnumerable<string> paths, RowId? before = null)
    {
        ThrowIfDisposed();
        var list = paths.ToList();
        if (list.Count == 0)
        {
            return Task.FromResult(new AddReport(Array.Empty<QueueRow>(), Array.Empty<Rejection>()));
        }
        var job = new AddJob(list, before: before);
        var start = Run(() =>
        {
            ThrowIfDisposed(); // again under the lock: a job joining a run Dispose ended would never be answered
            ObserveClock();
            var startWorker = Enqueue(job);
            Publish();
            return startWorker;
        });
        if (start)
        {
            StartAddWorker(AddWorker);
        }
        return job.Done.Task;
    }

    /// <summary>Joins <paramref name="job"/> to the run. Returns whether the caller starts the
    /// worker, once the lock is released: there is none yet.</summary>
    private bool Enqueue(AddJob job)
    {
        _addRun.Add(job);
        _addTotal += job.Remaining.Count;
        if (_addWorkerAlive)
        {
            return false;
        }
        _addWorkerAlive = true;
        return true;
    }

    /// <summary>Ends the current add at once, queued adds included: no further file is probed, a
    /// probe in flight is discarded, and the rows already added stay. Rejections met so far still
    /// arrive as the run's one notice. A no-op when idle.</summary>
    public void CancelAdd()
    {
        ThrowIfDisposed();
        Complete(Run(() =>
        {
            ObserveClock();
            if (_addRun.Count == 0)
            {
                return new List<AddCompletion>();
            }
            var done = EndAddRun(cancelled: true, notify: true);
            Publish();
            return done;
        }));
    }

    /// <summary>A file that throws is a rejection. Anything else that throws ends the run with a
    /// notice and the worker with it, rather than killing the process or leaving every later add
    /// waiting on a worker that is gone.</summary>
    private void AddWorker()
    {
        try
        {
            while (true)
            {
                var step = Run(TakeNextPath);
                if (step is null)
                {
                    return;
                }
                if (Directory.Exists(step.Path))
                {
                    Complete(Run(ListedOrRejected(step, ListFolder)));
                    continue;
                }
                if (step.Pending.Named && M3UPlaylist.IsPlaylist(step.Path) && File.Exists(step.Path))
                {
                    Complete(Run(ListedOrRejected(step, M3UPlaylist.Read)));
                    continue;
                }
                (Track? Track, Rejection? Rejection) resolved;
                try
                {
                    resolved = ResolvePath(step.Path);
                }
                catch (Exception e)
                {
                    resolved = (null, new Rejection(step.Path, null, RejectedProperty.File, e.Message));
                }
                Complete(Run(() => ApplyResolved(step, resolved)));
            }
        }
        catch (Exception e)
        {
            Complete(Run(() =>
            {
                _addWorkerAlive = false;
                RaiseNotice($"Adding stopped: {e.Message}");
                var done = EndAddRun(cancelled: true, notify: true);
                Publish();
                return done;
            }));
        }
    }

    /// <summary>The next path of the oldest job with any left, or null when there is none, in
    /// which case the worker ends (its end and the check are one locked step, so an add can never
    /// be left with no worker).</summary>
    private AddStep? TakeNextPath()
    {
        if (!_disposed)
        {
            foreach (var job in _addRun)
            {
                if (job.Remaining.Count > 0)
                {
                    var pending = job.Remaining.First!.Value;
                    job.Remaining.RemoveFirst();
                    return new AddStep(job, pending, _addRunId);
                }
            }
        }
        _addWorkerAlive = false;
        return null;
    }

    private List<AddCompletion> ApplyResolved(AddStep step, (Track? Track, Rejection? Rejection) resolved)
    {
        var done = new List<AddCompletion>();
        if (_disposed || step.RunId != _addRunId)
        {
            return done; // the run was cancelled while this file was being probed
        }
        ObserveClock();
        var job = step.Job;
        if (resolved.Track is { } track)
        {
            job.Added.Add(job.Before is null ? Append(track) : Place(track, job));
            _queueVersion++;
            RescheduleCurrentBoundary();
            if (!job.Opening)
            {
                SaveOpenPlaylist(throttled: true);
            }
        }
        else
        {
            job.Rejected.Add(resolved.Rejection!);
        }
        _addResolved++;
        return FinishStep(job, done);
    }

    /// <summary>Lists <paramref name="step"/>'s folder or playlist off the lock and returns the
    /// locked step that puts its files where it stood. One that cannot be listed is a rejection
    /// like a file that cannot be read.</summary>
    private Func<List<AddCompletion>> ListedOrRejected(AddStep step, Func<string, IReadOnlyList<string>> list)
    {
        try
        {
            var files = list(step.Path);
            return () => ApplyListed(step, files);
        }
        catch (Exception e)
        {
            var rejected = new Rejection(step.Path, null, RejectedProperty.File, e.Message);
            return () => ApplyResolved(step, (null, rejected));
        }
    }

    private List<AddCompletion> ApplyListed(AddStep step, IReadOnlyList<string> files)
    {
        if (_disposed || step.RunId != _addRunId)
        {
            return new List<AddCompletion>(); // the run was cancelled while this was listed
        }
        ObserveClock();
        for (var i = files.Count - 1; i >= 0; i--)
        {
            step.Job.Remaining.AddFirst(new PendingPath(files[i], Named: false));
        }
        _addTotal += files.Count - 1; // the folder or playlist counted as one path until now
        return FinishStep(step.Job, new List<AddCompletion>());
    }

    /// <summary>After a path is dealt with: answers its job if that was the job's last path, and
    /// ends the run if nothing is left anywhere. Publishes either way.</summary>
    private List<AddCompletion> FinishStep(AddJob job, List<AddCompletion> done)
    {
        if (job.Remaining.Count == 0 && !job.Reported)
        {
            done.Add(Report(job, cancelled: false));
            if (job == _loading)
            {
                LoadEnded();
            }
        }
        if (_addRun.All(j => j.Remaining.Count == 0))
        {
            done.AddRange(EndAddRun(cancelled: false, notify: true));
        }
        Publish();
        return done;
    }

    /// <summary>Closes the run: one notice for all of it if it refused anything, and a report for
    /// every caller not yet answered. Called under the lock.</summary>
    private List<AddCompletion> EndAddRun(bool cancelled, bool notify)
    {
        if (notify && _addRun.Any(j => j.Rejected.Count > 0))
        {
            RaiseNotice(new AddReport(
                _addRun.SelectMany(j => j.Added).ToList(),
                _addRun.SelectMany(j => j.Rejected).ToList(),
                cancelled).Summarise());
        }
        var done = _addRun.Where(j => !j.Reported).Select(j => Report(j, cancelled)).ToList();
        _addRun.Clear();
        _addResolved = 0;
        _addTotal = 0;
        _addRunId++;
        if (_loading is not null)
        {
            LoadEnded(); // cut short
        }
        if (_saveDeferred)
        {
            SaveOpenPlaylist(); // the add's last rows
        }
        return done;
    }

    /// <summary>Marks <paramref name="job"/> answered and freezes its report.</summary>
    private static AddCompletion Report(AddJob job, bool cancelled)
    {
        job.Reported = true;
        return new AddCompletion(job, new AddReport(job.Added.ToList(), job.Rejected.ToList(), cancelled));
    }

    /// <summary>Answers callers after the lock is released and the sink has been told, so a
    /// caller that waits on its task sees the notice and the rows already delivered.</summary>
    private static void Complete(List<AddCompletion> done)
    {
        foreach (var completion in done)
        {
            completion.Job.Done.TrySetResult(completion.Report);
        }
    }

    private static (Track? Track, Rejection? Rejection) ResolveFromDisk(string path)
    {
        var rejection = AudioProbe.Probe(path, out var probed);
        return rejection is not null ? (null, rejection) : (TrackMetadataReader.Read(path, probed!.Length, probed.Shape), null);
    }

    private AddProgress? Adding => _addTotal == 0 ? null : new AddProgress(_addResolved, _addTotal);
}
