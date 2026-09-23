using Faderio.Core.Domain;

namespace Faderio.Core.Playback;

public sealed partial class PlaybackEngine
{
    // ── Adding: probes off the lock, rows appended under it one at a time ───────────────

    /// <summary>One call to <see cref="Add"/>: its paths still to resolve, in order, and what
    /// became of the ones resolved so far. A folder is replaced in place by its files once listed.
    /// Touched only under the engine lock.</summary>
    private sealed class AddJob
    {
        public AddJob(IReadOnlyList<string> paths) => Remaining = new LinkedList<string>(paths);

        public LinkedList<string> Remaining { get; }
        public List<QueueRow> Added { get; } = new();
        public List<Rejection> Rejected { get; } = new();
        public bool Reported { get; set; }
        public TaskCompletionSource<AddReport> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>A path taken by the worker, and the run it was taken in.</summary>
    private sealed record AddStep(AddJob Job, string Path, long RunId);

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
    /// Queues files at the end of the queue, in the given order, and returns at once. A folder
    /// stands for every promised file under it (<see cref="FolderContents"/>), listed when the add
    /// reaches it and counted as one path until then; the shell never lists a directory. Each file
    /// is probed and its tags read off the engine lock and off the caller's thread; the lock is
    /// taken only to append its row. <see cref="PlayerView.Adding"/> counts the files resolved.
    /// A second add waits behind the first and shares its progress. When the run ends, one notice
    /// names every rejection with its reason (ADR 0007); a run that refused nothing is silent,
    /// since its rows are the news. Adding never starts playback. The task completes with this
    /// call's own report once its last file is in, after the sink has been told; the run's notice
    /// comes with the last add of the run.
    /// </summary>
    public Task<AddReport> Add(IEnumerable<string> paths)
    {
        ThrowIfDisposed();
        var list = paths.ToList();
        if (list.Count == 0)
        {
            return Task.FromResult(new AddReport(Array.Empty<QueueRow>(), Array.Empty<Rejection>()));
        }
        var job = new AddJob(list);
        var start = Run(() =>
        {
            ThrowIfDisposed(); // again under the lock: a job joining a run Dispose ended would never be answered
            ObserveClock();
            _addRun.Add(job);
            _addTotal += list.Count;
            Publish();
            if (_addWorkerAlive)
            {
                return false;
            }
            _addWorkerAlive = true;
            return true;
        });
        if (start)
        {
            StartAddWorker(AddWorker);
        }
        return job.Done.Task;
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
                    Complete(Run(ListedOrRejected(step)));
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
                    var path = job.Remaining.First!.Value;
                    job.Remaining.RemoveFirst();
                    return new AddStep(job, path, _addRunId);
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
            job.Added.Add(Append(track));
            _queueVersion++;
            RescheduleCurrentBoundary();
        }
        else
        {
            job.Rejected.Add(resolved.Rejection!);
        }
        _addResolved++;
        return FinishStep(job, done);
    }

    /// <summary>Lists <paramref name="step"/>'s folder off the lock and returns the locked step
    /// that puts its files where the folder stood. A folder that cannot be listed is a rejection
    /// like a file that cannot be read.</summary>
    private Func<List<AddCompletion>> ListedOrRejected(AddStep step)
    {
        try
        {
            var files = ListFolder(step.Path);
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
            return new List<AddCompletion>(); // the run was cancelled while this folder was listed
        }
        ObserveClock();
        for (var i = files.Count - 1; i >= 0; i--)
        {
            step.Job.Remaining.AddFirst(files[i]);
        }
        _addTotal += files.Count - 1; // the folder counted as one path until now
        return FinishStep(step.Job, new List<AddCompletion>());
    }

    /// <summary>After a path is dealt with: answers its job if that was the job's last path, and
    /// ends the run if nothing is left anywhere. Publishes either way.</summary>
    private List<AddCompletion> FinishStep(AddJob job, List<AddCompletion> done)
    {
        if (job.Remaining.Count == 0 && !job.Reported)
        {
            done.Add(Report(job, cancelled: false));
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
        return rejection is not null ? (null, rejection) : (TrackMetadataReader.Read(path, probed!.Length), null);
    }

    private AddProgress? Adding => _addTotal == 0 ? null : new AddProgress(_addResolved, _addTotal);
}
