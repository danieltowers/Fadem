using Fadem.Core.Domain;
using Fadem.Core.Playback;

namespace Fadem.Core.Tests;

/// <summary>
/// <c>Add(paths)</c> returns at once and probes off the engine lock, appending one row at a time
/// under it (build issue 01; restage tickets 07 and 12). Most tests run the add worker inline, so
/// the probe seam can observe and act at a known point in the add; the lock tests run it on a
/// real background thread, because the lock is re-entrant and only another thread can prove it
/// is free.
/// </summary>
public sealed class AddInTheBackgroundTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>Wraps the production resolve so a test can act while a probe is in flight.</summary>
    private static void OnProbe(TestEngine tc, Action<string> duringProbe)
    {
        var real = tc.Engine.ResolvePath;
        tc.Engine.ResolvePath = path =>
        {
            duringProbe(path);
            return real(path);
        };
    }

    [Fact]
    public void RowsAppear_InTheGivenOrder_AsTheyResolve_AndTheProgressCountsThem()
    {
        using var tc = TestEngine.Create();
        var files = Enumerable.Range(0, 4).Select(i => TestAudio.WriteDcWav($"f{i}.wav", 1, 0.1)).ToArray();
        var seen = new List<(int Rows, AddProgress? Progress)>();
        OnProbe(tc, _ => seen.Add((tc.View.Queue.Count, tc.View.Adding)));

        var report = tc.Engine.Add(files).Result;

        Assert.Equal(
            new[] { (0, 0), (1, 1), (2, 2), (3, 3) },
            seen.Select(s => (s.Rows, s.Progress!.Resolved)));
        Assert.All(seen, s => Assert.Equal(4, s.Progress!.Total));
        Assert.Equal(files, tc.View.Queue.Select(r => r.Track.FilePath));
        Assert.Equal(files, report.Added.Select(r => r.Track.FilePath));
        Assert.Null(tc.View.Adding);
    }

    [Fact]
    public void WhileTrackPlays_AddingFiftyFiles_NeverHoldsTheLockForAProbe()
    {
        using var tc = TestEngine.Create(backgroundAdd: true);
        var playing = TestAudio.WriteDcWav("playing.wav", 30, 0.4);
        var added = TestAudio.WriteDcWav("added.wav", 1, 0.2);
        tc.Play(playing);
        var blocked = 0;
        OnProbe(tc, _ =>
        {
            // The audio thread pulls a block while this probe is in flight. If the add held the
            // engine lock, the pull would wait for the probe, which waits for the pull.
            if (!Task.Run(() => tc.Output.Advance(441)).Wait(TimeSpan.FromSeconds(2)))
            {
                Interlocked.Increment(ref blocked);
            }
        });
        var reads = tc.Output.ReadCallCount;

        Assert.True(tc.Engine.Add(Enumerable.Repeat(added, 50)).Wait(Patience), "the add never completed");

        Assert.Equal(0, blocked);
        Assert.Equal(reads + 50, tc.Output.ReadCallCount);
        Assert.Equal(51, tc.View.Queue.Count);
        Assert.Equal(Transport.Playing, tc.View.Transport);
        Assert.Equal(playing, tc.View.Attended!.Row.Track.FilePath);
    }

    [Fact]
    public void Add_ReturnsBeforeAnyProbeFinishes()
    {
        using var tc = TestEngine.Create(backgroundAdd: true);
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.1);
        using var gate = new ManualResetEventSlim();
        OnProbe(tc, _ => gate.Wait(Patience));

        var adding = tc.Engine.Add(new[] { a, a });

        Assert.False(adding.IsCompleted);
        Assert.Equal(new AddProgress(0, 2), tc.View.Adding);
        Assert.Empty(tc.View.Queue);
        gate.Set();
        Assert.True(adding.Wait(Patience));
        Assert.Equal(2, tc.View.Queue.Count);
    }

    [Fact]
    public void CancelMidAdd_StopsFurtherProbing_AndKeepsTheRowsAlreadyAdded()
    {
        using var tc = TestEngine.Create();
        var files = Enumerable.Range(0, 5).Select(i => TestAudio.WriteDcWav($"c{i}.wav", 1, 0.1)).ToArray();
        var probed = new List<string>();
        OnProbe(tc, path =>
        {
            probed.Add(path);
            if (probed.Count == 3)
            {
                tc.Engine.CancelAdd(); // while the third file's probe is in flight
            }
        });

        var report = tc.Engine.Add(files).Result;

        Assert.Equal(files.Take(3), probed);
        Assert.Equal(files.Take(2), tc.View.Queue.Select(r => r.Track.FilePath)); // the third is discarded
        Assert.True(report.Cancelled);
        Assert.Equal(2, report.Added.Count);
        Assert.Null(tc.View.Adding);
        Assert.Empty(tc.Notices); // nothing was refused; the user cancelled and knows it
    }

    [Fact]
    public void CancelAfterARejection_StillReportsIt_AndSaysTheAddWasCancelled()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.1);
        var missing = Path.Combine(tc.TempDir, "gone.wav");
        OnProbe(tc, path =>
        {
            if (path == a)
            {
                tc.Engine.CancelAdd();
            }
        });

        var report = tc.Engine.Add(new[] { missing, a, a }).Result;

        Assert.Single(report.Rejected);
        var notice = Assert.Single(tc.Notices);
        Assert.StartsWith("Add cancelled. Nothing added: the file was rejected — gone.wav", notice);
    }

    [Fact]
    public void CancelWhileAProbeBlocks_LetsTheNextAddRun_AndDiscardsTheStaleProbe()
    {
        using var tc = TestEngine.Create(backgroundAdd: true);
        var stale = TestAudio.WriteDcWav("stale.wav", 1, 0.1);
        var fresh = TestAudio.WriteDcWav("fresh.wav", 1, 0.1);
        using var entered = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        OnProbe(tc, path =>
        {
            if (path == stale)
            {
                entered.Set();
                gate.Wait(Patience);
            }
        });
        var first = tc.Engine.Add(new[] { stale });
        Assert.True(entered.Wait(Patience));

        tc.Engine.CancelAdd();
        Assert.True(first.Wait(Patience)); // cancel answers at once, not when the probe returns
        Assert.Null(tc.View.Adding);
        var second = tc.Engine.Add(new[] { fresh });
        gate.Set();

        Assert.True(second.Wait(Patience));
        Assert.Equal(new[] { fresh }, tc.View.Queue.Select(r => r.Track.FilePath));
        Assert.True(first.Result.Cancelled);
        Assert.Empty(first.Result.Added);
    }

    [Fact]
    public void ASecondAdd_IssuedMidAdd_RunsAfterTheFirst_InOrder()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.1);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.1);
        var c = TestAudio.WriteDcWav("c.wav", 1, 0.1);
        var d = TestAudio.WriteDcWav("d.wav", 1, 0.1);
        var gone1 = Path.Combine(tc.TempDir, "gone1.wav");
        var gone2 = Path.Combine(tc.TempDir, "gone2.wav");
        Task<AddReport>? second = null;
        var progressAfterSecond = (AddProgress?)null;
        OnProbe(tc, path =>
        {
            if (path == b && second is null)
            {
                second = tc.Engine.Add(new[] { c, gone2, d });
                Assert.False(second.IsCompleted); // it waits behind the first
                progressAfterSecond = tc.View.Adding;
            }
        });

        var first = tc.Engine.Add(new[] { a, gone1, b }).Result;

        Assert.Equal(new[] { a, b, c, d }, tc.View.Queue.Select(r => r.Track.FilePath));
        Assert.Equal(new[] { a, b }, first.Added.Select(r => r.Track.FilePath));
        Assert.Equal(new[] { c, d }, second!.Result.Added.Select(r => r.Track.FilePath));
        Assert.Equal(new AddProgress(2, 6), progressAfterSecond);
        var notice = Assert.Single(tc.Notices); // one run, one notice, both adds' rejections
        Assert.Contains("gone1.wav", notice);
        Assert.Contains("gone2.wav", notice);
    }

    [Fact]
    public void Rejections_AreReportedOnce_OnCompletion_WithEachReason()
    {
        using var tc = TestEngine.Create();
        var good = TestAudio.WriteDcWav("good.wav", 1, 0.1);
        var quad = TestAudio.WriteWavAt("quad", 1, _ => 0.3, 44100, 16, 4);
        var missing = Path.Combine(tc.TempDir, "missing.wav");
        var noticesDuringAdd = new List<int>();
        OnProbe(tc, _ => noticesDuringAdd.Add(tc.Notices.Count));

        var report = tc.Engine.Add(new[] { missing, good, quad }).Result;

        Assert.All(noticesDuringAdd, n => Assert.Equal(0, n));
        Assert.Equal(2, report.Rejected.Count);
        var notice = Assert.Single(tc.Notices);
        Assert.Contains("missing.wav: The file does not exist.", notice);
        Assert.Contains("4 channels", notice);
        Assert.Contains("supported-audio.md", notice);
        Assert.Equal(new[] { good }, tc.View.Queue.Select(r => r.Track.FilePath));
    }

    [Fact]
    public void AResolveThatThrows_IsARejection_NotALostAdd()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.1);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.1);
        OnProbe(tc, path =>
        {
            if (path == a)
            {
                throw new IOException("The process cannot access the file.");
            }
        });

        var report = tc.Engine.Add(new[] { a, b }).Result;

        var rejection = Assert.Single(report.Rejected);
        Assert.Equal(RejectedProperty.File, rejection.Property);
        Assert.Contains("cannot access", rejection.Detail);
        Assert.Equal(new[] { b }, tc.View.Queue.Select(r => r.Track.FilePath));
    }

    [Fact]
    public void AFailureOutsideTheProbe_EndsTheRunWithANotice_AndTheNextAddStillRuns()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.1);
        var thrown = false;
        var record = tc.Sink.OnChanged;
        tc.Sink.OnChanged = view =>
        {
            record?.Invoke(view);
            if (!thrown && view.Queue.Count == 1)
            {
                thrown = true;
                throw new InvalidOperationException("a handler failed");
            }
        };

        var first = tc.Engine.Add(new[] { a, a, a });

        Assert.True(first.IsCompleted);
        Assert.True(first.Result.Cancelled);
        Assert.Contains(tc.Notices, n => n.Contains("Adding stopped: a handler failed"));
        Assert.Null(tc.View.Adding);
        Assert.Single(tc.View.Queue);
        tc.Engine.Add(new[] { a }).Wait();
        Assert.Equal(2, tc.View.Queue.Count);
    }

    [Fact]
    public void AddingToAnEmptyStoppedQueue_DoesNotStartPlayback()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.1);

        tc.Engine.Add(new[] { a }).Wait();

        Assert.Equal(Transport.Stopped, tc.View.Transport);
        Assert.Equal(Transport.Stopped, tc.Output.State);
        Assert.Null(tc.View.Attended);
        Assert.Single(tc.View.Queue);
    }

    [Fact]
    public void AddingNothing_IsNotARun()
    {
        using var tc = TestEngine.Create();

        var report = tc.Engine.Add(Array.Empty<string>()).Result;

        Assert.Empty(report.Added);
        Assert.Null(tc.View.Adding);
        Assert.Empty(tc.Notices);
    }

    [Fact]
    public void DisposingMidAdd_EndsTheAdd_AndTheStaleProbeIsDropped()
    {
        var tc = TestEngine.Create(backgroundAdd: true);
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.1);
        using var entered = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        OnProbe(tc, _ =>
        {
            entered.Set();
            gate.Wait(Patience);
        });
        var adding = tc.Engine.Add(new[] { a, a });
        Assert.True(entered.Wait(Patience));

        tc.Dispose();
        gate.Set();

        Assert.True(adding.Wait(Patience));
        Assert.True(adding.Result.Cancelled);
        Assert.Empty(adding.Result.Added);
    }

    private static Rejection Rejected(string path) =>
        new(path, new AudioShape(AudioFormat.Wav, 44100, 16, 4), RejectedProperty.Channels,
            SupportedAudio.Describe(RejectedProperty.Channels, new AudioShape(AudioFormat.Wav, 44100, 16, 4)));

    [Fact]
    public void Summarise_NamesEveryRejection_InEveryCase()
    {
        var row = new QueueRow(new RowId(1), new Track { FilePath = @"C:\a.wav" }, true);

        var mixed = new AddReport(new[] { row }, new[] { Rejected(@"C:\quad.wav") }).Summarise();
        Assert.StartsWith("Added 1 track; rejected 1", mixed);
        Assert.Contains("quad.wav: 4 channels is not promised", mixed);
        Assert.EndsWith("See docs/supported-audio.md.", mixed);

        // v2 gave no summary at all when every file failed.
        var none = new AddReport(Array.Empty<QueueRow>(), new[] { Rejected(@"C:\x.wav"), Rejected(@"C:\y.wav") }).Summarise();
        Assert.StartsWith("Nothing added: all 2 files were rejected", none);

        Assert.StartsWith("Add cancelled. Added 1 track; rejected 1",
            new AddReport(new[] { row }, new[] { Rejected(@"C:\quad.wav") }, true).Summarise());
    }
}
