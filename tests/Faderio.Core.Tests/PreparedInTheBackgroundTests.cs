using Faderio.Core.Domain;
using Faderio.Core.Playback;

namespace Faderio.Core.Tests;

/// <summary>The boundary's incoming track is opened in the background and held in memory (phase 6
/// issue 10), through the seam. The background is the test's own: each open the engine starts is
/// held back until the test lets it run, so a slow disk is a job not yet run, with no thread and
/// no waiting. No file is ever opened while the device is being fed.</summary>
public sealed class PreparedInTheBackgroundTests
{
    private const int Second = 44100;

    /// <summary>The engine's background, in the test's hands: the opens it has started and not
    /// yet run, and every file opened while a block was being rendered.</summary>
    private sealed class Background
    {
        private readonly TestEngine _tc;
        private readonly Queue<Action> _waiting = new();
        private bool _rendering;

        public Background(TestEngine tc)
        {
            _tc = tc;
            tc.Engine.StartPrepareWorker = _waiting.Enqueue;
            tc.Engine.TrackFileOpening = path =>
            {
                Opened.Add(path);
                if (_rendering)
                {
                    OpenedWhileRendering.Add(path);
                }
                WhileOpening?.Invoke(path);
            };
        }

        /// <summary>Every file the engine opened, in order, wherever it did.</summary>
        public List<string> Opened { get; } = new();

        public List<string> OpenedWhileRendering { get; } = new();

        /// <summary>What else happens while a file is being opened: the engine is not locked.</summary>
        public Action<string>? WhileOpening { get; set; }

        /// <summary>The device pulls <paramref name="frames"/> frames; the background stands still.</summary>
        public void Render(int frames)
        {
            _rendering = true;
            try
            {
                _tc.Output.Advance(frames);
            }
            finally
            {
                _rendering = false;
            }
        }

        /// <summary>Every open started so far runs to its end, between two blocks.</summary>
        public void CatchUp()
        {
            for (var run = 0; _waiting.Count > 0; run++)
            {
                Assert.True(run < 100, "The engine never stops starting opens.");
                _waiting.Dequeue()();
            }
        }
    }

    [Fact]
    public void NoFileIsOpenedWhileRendering_AndTheBoundariesLandWhereTheyDid()
    {
        using var tc = TestEngine.Create();
        var background = new Background(tc);
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 1, 0.3);
        tc.Play(a, b, c);
        background.CatchUp();

        background.Render(Second + Second / 2); // A → B: B's own boundary is scheduled mid-block
        background.CatchUp();
        background.Render(Second); // B → C

        Assert.Empty(background.OpenedWhileRendering);
        Assert.Equal(0.4, tc.Rendered.Sample(Second - 1, 0), 2);
        Assert.Equal(0.2, tc.Rendered.Sample(Second, 0), 2);
        Assert.Equal(0.2, tc.Rendered.Sample(Second * 2 - 1, 0), 2);
        Assert.Equal(0.3, tc.Rendered.Sample(Second * 2, 0), 2);
        Assert.Equal(c, tc.CurrentTrack!.FilePath);
        Assert.Empty(tc.Notices);
    }

    [Fact]
    public void AnOpenThatOutlastsManyBlocks_ButEndsBeforeItsFadeOutPoint_ChangesNoSample()
    {
        var a = TestAudio.WriteToneWav("a.wav", 3, FA);
        var b = TestAudio.WriteToneWav("b.wav", 3, FB);
        float[] Rendered(bool slowly)
        {
            using var tc = TestEngine.Create();
            var background = new Background(tc);
            tc.Add(a, b);
            tc.Store.Upsert(a, b, CrossfadeAt(1, 1));
            tc.Engine.Play();
            if (slowly)
            {
                background.Render(Second * 9 / 10); // nine device buffers go by with B unread
            }
            background.CatchUp();
            background.Render(Second * 4 - (slowly ? Second * 9 / 10 : 0));
            Assert.Empty(tc.Notices);
            Assert.Empty(background.OpenedWhileRendering);
            return tc.Output.Blocks.SelectMany(block => block).ToArray();
        }

        var atOnce = Rendered(slowly: false);
        var slow = Rendered(slowly: true);

        var heard = new RenderedAudio(new List<float[]> { slow });
        var (midA, midB) = Gains(heard, Second + Second / 2); // the fade, where it is authored
        Assert.InRange(midA, 0.6, 0.8);
        Assert.InRange(midB, 0.6, 0.8);
        Assert.Equal(atOnce, slow);
    }

    // ── Not ready in time ────────────────────────────────────────────────────────────────

    private const double FA = 1000;
    private const double FB = 2000;

    private static TransitionSpec CrossfadeAt(double point, double duration) => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(point), FromEnd: false),
        FadeInPoint = TimeSpan.Zero,
        FadeOutDuration = TimeSpan.FromSeconds(duration),
        FadeInDuration = TimeSpan.FromSeconds(duration),
    };

    /// <summary>Normalised gains of both voices at a frame, via frequency separation.</summary>
    private static (double GA, double GB) Gains(RenderedAudio rendered, long endFrame) =>
        (rendered.ToneAmplitude(FA, endFrame) / 0.4, rendered.ToneAmplitude(FB, endFrame) / 0.4);

    [Fact]
    public void AnOpenStillRunningAtTheFadeOutPoint_LetsTheOutgoingTrackPlayOn_AndTheTransitionBeginsWhenItIsReady()
    {
        using var tc = TestEngine.Create();
        var background = new Background(tc);
        var a = TestAudio.WriteToneWav("a.wav", 5, FA);
        var b = TestAudio.WriteToneWav("b.wav", 5, FB);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, CrossfadeAt(1, 1));
        tc.Engine.Play();

        background.Render(Second * 2); // past the whole authored fade, B still being read
        var (alone, none) = Gains(tc.Rendered, Second * 2);
        background.CatchUp(); // B is ready at 2 s
        background.Render(Second * 2);

        Assert.InRange(alone, 0.97, 1.03); // A played on at full volume: no fade into nothing, no gap
        Assert.InRange(none, 0, 0.03);
        var (midA, midB) = Gains(tc.Rendered, Second * 2 + Second / 2); // mid-fade, a second late
        Assert.InRange(midA, 0.6, 0.8);
        Assert.InRange(midB, 0.6, 0.8);
        var (goneA, fullB) = Gains(tc.Rendered, Second * 3 + Second / 2);
        Assert.InRange(goneA, 0, 0.03);
        Assert.InRange(fullB, 0.97, 1.03);
        Assert.Equal(b, tc.CurrentTrack!.FilePath);
        var notice = Assert.Single(tc.Notices);
        Assert.Contains(Path.GetFileName(b), notice);
        Assert.Contains("1.0 s late", notice);
        Assert.Empty(background.OpenedWhileRendering);
    }

    [Fact]
    public void ALateTransition_WithLessOfTheOutgoingTrackLeftThanItsFade_FadesOverWhatIsLeft()
    {
        // The fade as authored runs to the end of A, as a default crossfade does: any lateness
        // leaves less than its length.
        using var tc = TestEngine.Create();
        var background = new Background(tc);
        var a = TestAudio.WriteToneWav("a.wav", 3, FA);
        var b = TestAudio.WriteToneWav("b.wav", 5, FB);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, CrossfadeAt(2, 1));
        tc.Engine.Play();

        background.Render(Second * 2 + Second / 2); // half the authored fade has gone by
        background.CatchUp();
        background.Render(Second * 2);

        var (startA, _) = Gains(tc.Rendered, Second * 2 + Second / 2);
        Assert.InRange(startA, 0.97, 1.03);
        var (midA, midB) = Gains(tc.Rendered, Second * 2 + Second * 3 / 4); // half-way through 0.5 s
        Assert.InRange(midA, 0.6, 0.8);
        Assert.InRange(midB, 0.6, 0.8); // B's fade-in shortens with it: no dip between them
        var (goneA, fullB) = Gains(tc.Rendered, Second * 3 + Second / 4);
        Assert.InRange(goneA, 0, 0.03);
        Assert.InRange(fullB, 0.97, 1.03);
        Assert.Single(tc.Notices);
    }

    [Fact]
    public void AnOpenStillRunningWhenTheOutgoingTrackEnds_IsSilence_UntilTheIncomingTrackStarts()
    {
        // Nothing is left to play on, and rendering never waits: the gap is the disk's.
        using var tc = TestEngine.Create();
        var background = new Background(tc);
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.2);
        tc.Play(a, b);

        background.Render(Second + Second / 2);
        var waiting = tc.View;
        background.CatchUp(); // B is ready half a second after A ended
        background.Render(Second / 2);

        Assert.Equal(Transport.Playing, waiting.Transport);
        Assert.Equal(a, waiting.Attended!.Row.Track.FilePath);
        Assert.Equal(TimeSpan.FromSeconds(1), waiting.Attended.Playhead.TimeSpan);
        Assert.False(waiting.CanTransitionNow);
        Assert.False(waiting.CanCueTransition);
        Assert.Equal(0.4, tc.Rendered.Sample(Second - 1, 0), 2);
        Assert.Equal(0.0, tc.Rendered.Sample(Second, 0), 2);
        Assert.Equal(0.0, tc.Rendered.Sample(Second + Second / 2 - 1, 0), 2);
        Assert.Equal(0.2, tc.Rendered.Sample(Second + Second / 2, 0), 2); // B from its first frame
        Assert.Equal(b, tc.CurrentTrack!.FilePath);
        Assert.Equal(TimeSpan.FromSeconds(0.5), tc.Playhead.TimeSpan);
        var notice = Assert.Single(tc.Notices);
        Assert.Contains(Path.GetFileName(b), notice);
        Assert.Contains("0.5 s of silence", notice);
        Assert.Empty(background.OpenedWhileRendering);

        background.Render(Second); // B plays out, and the queue ends as it always did
        Assert.Equal(0.2, tc.Rendered.Sample(Second * 2 + Second / 2 - 1, 0), 2);
        Assert.Equal(Transport.Stopped, tc.View.Transport);
        Assert.Equal(1, tc.PlaybackFinishedCount);
    }

    // ── A file that cannot be read ───────────────────────────────────────────────────────

    [Fact]
    public void AnOpenThatFailsBeforeItsBoundary_MarksTheRow_AndTheBoundaryGoesToTheNextRowWithItsOwnTransition()
    {
        using var tc = TestEngine.Create();
        var background = new Background(tc);
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 3, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 3, 0.3);
        tc.Add(a, b, c);
        tc.Store.Upsert(a, b, CrossfadeAt(1, 1));
        tc.Store.Upsert(a, c, CrossfadeAt(2, 1));
        File.Delete(b);
        tc.Engine.Play();
        background.Render(Second / 2);
        Assert.Empty(tc.Notices); // nothing has looked at B yet

        background.CatchUp();
        background.Render(Second * 2);

        Assert.Equal(0.4, tc.Rendered.DcLevel(Second + Second / 2), 2); // A → B's fade never starts
        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath); // mid A → C's crossfade
        Assert.Equal(c, tc.View.Incoming!.Row.Track.FilePath);
        var notice = Assert.Single(tc.Notices);
        Assert.Contains(Path.GetFileName(b), notice);
        Assert.Equal(new[] { false, true, false }, tc.View.Queue.Select(r => r.IsUnreadable));
        Assert.Empty(background.OpenedWhileRendering);
    }

    [Fact]
    public void AnOpenThatFailsAfterItsFadeOutPoint_CutsToTheNextRowAtTheOutgoingTracksEnd()
    {
        // A → C is unauthored: a hard cut at A's end, as its own resolution has it.
        using var tc = TestEngine.Create();
        var background = new Background(tc);
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 3, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 3, 0.3);
        tc.Add(a, b, c);
        tc.Store.Upsert(a, b, CrossfadeAt(1, 1));
        File.Delete(b);
        tc.Engine.Play();

        background.Render(Second + Second / 2); // late: B is still being read
        background.CatchUp();
        background.Render(Second * 2);

        Assert.Equal(0.4, tc.Rendered.Sample(Second * 3 - 1, 0), 2);
        Assert.Equal(0.3, tc.Rendered.Sample(Second * 3, 0), 2);
        Assert.Equal(c, tc.CurrentTrack!.FilePath);
        Assert.Contains(Path.GetFileName(b), Assert.Single(tc.Notices));
    }

    [Fact]
    public void AnOpenThatFailsAfterTheOutgoingTrackEnded_GoesToTheNextRow()
    {
        using var tc = TestEngine.Create();
        var background = new Background(tc);
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 1, 0.3);
        tc.Add(a, b, c);
        File.Delete(b);
        tc.Engine.Play();

        background.Render(Second + Second / 2); // A has ended: silence, B still being read
        background.CatchUp(); // B fails, then C opens
        background.Render(Second / 2);

        Assert.Equal(0.3, tc.Rendered.Sample(Second + Second / 2, 0), 2);
        Assert.Equal(c, tc.CurrentTrack!.FilePath);
        Assert.Equal(2, tc.Notices.Count);
        Assert.Contains(Path.GetFileName(b), tc.Notices[0]);
        Assert.Contains(Path.GetFileName(c), tc.Notices[1]);
        Assert.Equal(new[] { false, true, false }, tc.View.Queue.Select(r => r.IsUnreadable));
    }

    [Fact]
    public void AnOpenThatFailsAfterTheOutgoingTrackEnded_WithNoRowLeft_FinishesPlayback()
    {
        using var tc = TestEngine.Create();
        var background = new Background(tc);
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.2);
        tc.Add(a, b);
        File.Delete(b);
        tc.Engine.Play();

        background.Render(Second + Second / 2);
        background.CatchUp();
        background.Render(Second / 2);

        Assert.Equal(Transport.Stopped, tc.View.Transport);
        Assert.Equal(1, tc.PlaybackFinishedCount);
        Assert.Contains(Path.GetFileName(b), Assert.Single(tc.Notices));
    }

    [Fact]
    public void WithEveryFileGone_UnderRepeatAll_EachRowIsTriedOnce_AndPlaybackFinishes()
    {
        using var tc = TestEngine.Create();
        var background = new Background(tc);
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 1, 0.3);
        tc.Add(a, b, c);
        tc.Engine.SetRepeat(Faderio.Core.Queue.RepeatMode.All);
        tc.Engine.Play();
        File.Delete(a);
        File.Delete(b);
        File.Delete(c);

        background.CatchUp(); // B, then C, then round to A: none opens
        background.Render(Second + Second / 2);

        Assert.Equal(3, tc.Notices.Count);
        Assert.Equal(0.4, tc.Rendered.Sample(Second - 1, 0), 2); // A, already open, played out
        Assert.Equal(Transport.Stopped, tc.View.Transport);
        Assert.Equal(1, tc.PlaybackFinishedCount);
    }

    // ── The queue moves on while a track is being read ───────────────────────────────────

    [Fact]
    public void ASkipToATrackStillBeingRead_PlaysItAtOnce()
    {
        // A command needs its track now, and opens it itself.
        using var tc = TestEngine.Create();
        var background = new Background(tc);
        var a = TestAudio.WriteDcWav("a.wav", 2, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 2, 0.2);
        tc.Play(a, b);
        background.Render(Second / 2);

        tc.Engine.Next();
        background.Render(Second / 2);
        background.CatchUp();
        background.Render(Second / 2);

        Assert.Equal(0.2, tc.Rendered.Sample(Second / 2, 0), 2);
        Assert.Equal(b, tc.CurrentTrack!.FilePath);
        Assert.Equal(TimeSpan.FromSeconds(1), tc.Playhead.TimeSpan);
        Assert.Empty(tc.Notices);
    }

    [Fact]
    public void ASeek_KeepsTheTrackBeingRead_AndDoesNotOpenItAgain()
    {
        using var tc = TestEngine.Create();
        var background = new Background(tc);
        var a = TestAudio.WriteDcWav("a.wav", 2, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 2, 0.2);
        tc.Play(a, b);

        tc.Engine.Seek(Playhead.FromMilliseconds(1500));
        background.CatchUp();
        background.Render(Second);

        Assert.Equal(new[] { a, b }, background.Opened);
        Assert.Equal(0.2, tc.Rendered.Sample(Second / 2, 0), 2); // the boundary, where the seek put it
        Assert.Empty(tc.Notices);
    }

    [Fact]
    public void RemovingTheRowBeingRead_SendsTheBoundaryToTheRowAfterIt_AndClosesTheFileWhenItsOpenEnds()
    {
        using var tc = TestEngine.Create();
        var background = new Background(tc);
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 1, 0.3);
        tc.Engine.PreparedMemoryCap = 0; // read from disk as it plays, so an open left behind holds the file
        tc.Play(a, b, c);
        background.WhileOpening = path =>
        {
            if (path == b)
            {
                tc.Engine.Remove(new[] { tc.RowOf(b) }); // while B's open is under way
            }
        };

        background.CatchUp();
        background.Render(Second + Second / 2);

        Assert.Equal(0.3, tc.Rendered.Sample(Second, 0), 2);
        Assert.Equal(c, tc.CurrentTrack!.FilePath);
        Assert.Empty(tc.Notices);
        using (new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // nothing holds B open
        }
    }

    [Fact]
    public void AQueueChangeThatLeavesTheNextRowAlone_WhileItsTransitionIsLate_DoesNotLoseTheTransition()
    {
        using var tc = TestEngine.Create();
        var background = new Background(tc);
        var a = TestAudio.WriteToneWav("a.wav", 5, FA);
        var b = TestAudio.WriteToneWav("b.wav", 5, FB);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, CrossfadeAt(1, 1));
        tc.Engine.Play();
        background.Render(Second * 2); // late

        var d = TestAudio.WriteToneWav("d.wav", 1, 500);
        tc.Add(d);
        background.CatchUp();
        background.Render(Second);

        var (midA, midB) = Gains(tc.Rendered, Second * 2 + Second / 2);
        Assert.InRange(midA, 0.6, 0.8);
        Assert.InRange(midB, 0.6, 0.8);
    }

    [Fact]
    public void ATransitionEditedWhileItIsLate_PlaysAsEdited()
    {
        using var tc = TestEngine.Create();
        var background = new Background(tc);
        var a = TestAudio.WriteToneWav("a.wav", 6, FA);
        var b = TestAudio.WriteToneWav("b.wav", 6, FB);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, CrossfadeAt(1, 1));
        tc.Engine.Play();
        background.Render(Second * 2); // late

        tc.Engine.Author(tc.AdjacencyOf(a, b), CrossfadeAt(1, 2));
        background.CatchUp();
        background.Render(Second * 2);

        var (midA, midB) = Gains(tc.Rendered, Second * 3); // half-way through two seconds, not one
        Assert.InRange(midA, 0.6, 0.8);
        Assert.InRange(midB, 0.6, 0.8);
    }

    [Fact]
    public void RemovingTheAwaitedRow_AwaitsTheRowAfterIt()
    {
        using var tc = TestEngine.Create();
        var background = new Background(tc);
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 1, 0.3);
        tc.Play(a, b, c);
        background.Render(Second + Second / 2); // A has ended: silence, B still being read

        tc.Engine.Remove(new[] { tc.RowOf(b) });
        background.CatchUp();
        background.Render(Second / 2);

        Assert.Equal(0.3, tc.Rendered.Sample(Second + Second / 2, 0), 2);
        Assert.Equal(c, tc.CurrentTrack!.FilePath);
        Assert.Contains(Path.GetFileName(c), Assert.Single(tc.Notices));
    }

    // ── Held in memory ───────────────────────────────────────────────────────────────────

    [Fact]
    public void TheNextTrack_IsHeldInMemory_SoItPlaysWithItsFileGoneFromUnderIt()
    {
        using var tc = TestEngine.Create();
        var background = new Background(tc);
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.2);
        tc.Play(a, b);
        background.CatchUp();

        File.WriteAllBytes(b, Array.Empty<byte>()); // nothing holds it open, and nothing is left in it
        background.Render(Second * 2);

        Assert.Equal(0.2, tc.Rendered.Sample(Second, 0), 2);
        Assert.Equal(0.2, tc.Rendered.Sample(Second * 2 - 1, 0), 2);
        Assert.Empty(tc.Notices);
    }

    [Fact]
    public void AFileOverTheMemoryCap_IsStillOpenedInTheBackground_AndReadFromDiskAsItPlays()
    {
        using var tc = TestEngine.Create();
        var background = new Background(tc);
        tc.Engine.PreparedMemoryCap = 1024;
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.2);
        tc.Play(a, b);
        background.CatchUp();

        Assert.Throws<IOException>(() => new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.None).Dispose());
        background.Render(Second + Second / 2);

        Assert.Equal(0.2, tc.Rendered.Sample(Second, 0), 2);
        Assert.Empty(tc.Notices);
        Assert.Empty(background.OpenedWhileRendering);
    }

    [Theory]
    [InlineData("noise-44100-16-2.flac")]
    [InlineData("noise-44100-24-1.flac")]
    [InlineData("noise-cbrinfo-44100-2.mp3")]
    [InlineData("noise-vbr-44100-2.mp3")]
    [InlineData("noise-cbrbare-44100-1.mp3")]
    public void ATrackHeldInMemory_DecodesExactlyAsItDoesFromDisk(string fixture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
        Assert.True(File.Exists(path), $"fixture {fixture} is missing; run Fixtures/generate.sh");

        var fromDisk = RenderedInto(path, memoryCap: 0);
        var fromMemory = RenderedInto(path, memoryCap: long.MaxValue);

        Assert.True(fromDisk.Skip(Second).Any(sample => sample != 0), "the fixture never sounded");
        Assert.Equal(fromDisk, fromMemory);
    }

    /// <summary>A second of silence, then <paramref name="next"/> to its end: every sample.</summary>
    private static float[] RenderedInto(string next, long memoryCap)
    {
        using var tc = TestEngine.Create();
        tc.Engine.PreparedMemoryCap = memoryCap;
        tc.Play(TestAudio.WriteDcWav("lead.wav", 1, 0), next);
        tc.Output.Advance(Second * 4);
        return tc.Output.Blocks.SelectMany(block => block).ToArray();
    }

    // ── The engine's own background ──────────────────────────────────────────────────────

    [Fact]
    public void LeftToItself_TheEngineOpensTheNextTrackOnAThreadOfItsOwn()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.2);
        Thread? opener = null;
        tc.Engine.StartPrepareWorker = null; // as the app runs it
        tc.Engine.TrackFileOpening = path =>
        {
            if (path == b)
            {
                Volatile.Write(ref opener, Thread.CurrentThread);
            }
        };

        tc.Play(a, b);
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref opener) is not null, TimeSpan.FromSeconds(10)));
        Assert.NotSame(Thread.CurrentThread, opener);
        Assert.True(opener!.Join(TimeSpan.FromSeconds(10))); // its answer is in
        tc.Output.Advance(Second + Second / 2);

        Assert.Equal(0.2, tc.Rendered.Sample(Second, 0), 2);
        Assert.Empty(tc.Notices);
    }

    [Fact]
    public void DisposingTheEngine_WhileATrackIsBeingRead_ClosesItWhenItsOpenEnds()
    {
        var tc = TestEngine.Create();
        var background = new Background(tc);
        tc.Engine.PreparedMemoryCap = 0; // read from disk as it plays, so an open left behind holds the file
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.2);
        tc.Play(a, b);
        background.WhileOpening = _ => tc.Dispose(); // while B's open is under way

        background.CatchUp();

        using (new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // nothing holds B open
        }
    }
}
