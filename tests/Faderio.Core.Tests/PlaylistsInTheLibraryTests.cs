using System.Text;
using Faderio.Core.Domain;
using Faderio.Core.Persistence;
using Faderio.Core.Playback;
using Faderio.Core.Settings;
using Faderio.Core.Transitions;

namespace Faderio.Core.Tests;

/// <summary>
/// Playlists issue 01 (D14–D17, D20): a playlist is a <c>.m3u8</c> in the library's
/// <c>.faderio/playlists/</c>, exactly one is open, the queue mirrors it and every change to the
/// queue's rows is written to it. Opening one replaces the queue, and a voice still sounding
/// finishes first, its row → the new row 0 being the next adjacency (the join).
/// </summary>
public sealed class PlaylistsInTheLibraryTests
{
    private const int Rate = 44100;

    private static int At(double seconds) => (int)(seconds * Rate);

    private static string Tone(string name, double seconds = 4, double freq = 1000) =>
        TestAudio.WriteToneWav($"pl01-{name}.wav", seconds, freq);

    private static string PlaylistPath(TestEngine tc, string name) =>
        Path.Combine(tc.Library.PlaylistsFolder, name + ".m3u8");

    /// <summary>The playlist's file, as its entries, in order.</summary>
    private static IReadOnlyList<string> Entries(TestEngine tc, string name) => M3UPlaylist.Read(PlaylistPath(tc, name));

    /// <summary>A playlist written straight into the folder, as if kept from an earlier run.</summary>
    private static void WritePlaylist(TestEngine tc, string name, params string[] paths) =>
        File.WriteAllLines(PlaylistPath(tc, name), ["#EXTM3U", .. paths], new UTF8Encoding(false));

    private static string[] Queued(TestEngine tc) => tc.View.Queue.Select(r => r.Track.FilePath).ToArray();

    private static TransitionSpec Crossfade(double outPoint) => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(outPoint), FromEnd: false),
        FadeOutDuration = TimeSpan.FromSeconds(1),
        FadeInPoint = TimeSpan.Zero,
        FadeInDuration = TimeSpan.FromSeconds(1),
    };

    // ── The store and autosave ───────────────────────────────────────────────────────────

    [Fact]
    public void AFreshLibrary_OpensAnUntitled_ThatIsTheOnlyPlaylist()
    {
        using var tc = TestEngine.Create();

        Assert.Equal("Untitled", tc.View.OpenPlaylist);
        Assert.Equal(new[] { "Untitled" }, tc.View.Playlists);
        Assert.Empty(Entries(tc, "Untitled"));
    }

    [Fact]
    public void AddThreeFiles_WithNoPlaylistOpened_WritesThemToUntitled()
    {
        using var tc = TestEngine.Create();
        var files = new[] { Tone("add-a"), Tone("add-b"), Tone("add-c") };

        tc.Add(files);

        Assert.Equal(files, Entries(tc, "Untitled"));
    }

    [Fact]
    public void AnAddInTheBackground_HasWrittenItsRows_ByTheTimeItsTaskCompletes()
    {
        using var tc = TestEngine.Create(backgroundAdd: true);
        var files = new[] { Tone("bg-a"), Tone("bg-b"), Tone("bg-c") };

        tc.Engine.Add(files).Wait();

        Assert.Equal(files, Entries(tc, "Untitled"));
    }

    [Fact]
    public void MoveRemoveAndClear_EachWriteTheOpenPlaylist()
    {
        using var tc = TestEngine.Create();
        var (a, b, c) = (Tone("edit-a"), Tone("edit-b"), Tone("edit-c"));
        tc.Add(a, b, c);

        tc.Engine.Move([tc.RowOf(c)], tc.RowOf(a));
        Assert.Equal(new[] { c, a, b }, Entries(tc, "Untitled"));

        tc.Engine.Remove([tc.RowOf(a)]);
        Assert.Equal(new[] { c, b }, Entries(tc, "Untitled"));

        tc.Engine.Clear();
        Assert.Empty(Entries(tc, "Untitled"));
    }

    [Fact]
    public void UnderShuffle_TheFileKeepsThePlaylistsOwnOrder()
    {
        using var tc = TestEngine.Create(new PlayerSettings { Shuffle = true }, rngSeed: 7);
        var files = Enumerable.Range(0, 8).Select(i => Tone($"shuffle-{i}", 1)).ToArray();

        tc.Add(files);

        Assert.NotEqual(files, Queued(tc)); // the queue plays shuffled
        Assert.Equal(files, Entries(tc, "Untitled"));
        tc.Engine.Move([tc.View.Queue[^1].Id], tc.View.Queue[1].Id);
        Assert.Equal(files, Entries(tc, "Untitled")); // a move under shuffle is the shuffle's
    }

    [Fact]
    public void AFileThatCannotBeWritten_IsANotice_AndTheQueueStaysAsItIs()
    {
        using var tc = TestEngine.Create();
        File.Delete(PlaylistPath(tc, "Untitled"));
        Directory.CreateDirectory(PlaylistPath(tc, "Untitled")); // a folder where the file goes: every write fails
        var (a, b) = (Tone("fail-a"), Tone("fail-b"));

        tc.Add(a, b);

        Assert.Equal(new[] { a, b }, Queued(tc));
        Assert.StartsWith("The playlist Untitled could not be saved:", Assert.Single(tc.Notices));
    }

    // ── Opening, and the join ────────────────────────────────────────────────────────────

    [Fact]
    public void OpenPlaylist_WhileStopped_ReplacesTheQueue_RowZeroAttendedAtTheStart()
    {
        using var tc = TestEngine.Create();
        tc.Add(Tone("stop-a0"), Tone("stop-a1"));
        var (b0, b1) = (Tone("stop-b0"), Tone("stop-b1"));
        WritePlaylist(tc, "B", b0, b1);

        tc.Engine.OpenPlaylist("B").Wait();

        Assert.Equal("B", tc.View.OpenPlaylist);
        Assert.Equal(new[] { b0, b1 }, Queued(tc));
        Assert.True(tc.View.Queue[0].IsCurrent);
        Assert.Equal(Transport.Stopped, tc.View.Transport);
        tc.Engine.Play();
        Assert.Equal(b0, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(Playhead.Zero, tc.View.Attended.Playhead);
    }

    [Fact]
    public void OpenB_WhileAsRowTwoPlays_RowTwoPlaysOn_IntoBsRowZero_ByTheAuthoredTransition()
    {
        using var tc = TestEngine.Create();
        var (a0, a1, a2) = (Tone("join-a0"), Tone("join-a1"), Tone("join-a2", 4, 1000));
        var (b0, b1) = (Tone("join-b0", 4, 2000), Tone("join-b1"));
        WritePlaylist(tc, "A", a0, a1, a2);
        WritePlaylist(tc, "B", b0, b1);
        tc.Store.Upsert(a2, b0, Crossfade(outPoint: 2)); // the join's own pair
        tc.Engine.OpenPlaylist("A").Wait();
        tc.Engine.PlayRow(tc.RowOf(a2));
        tc.Output.Advance(At(0.5));

        tc.Engine.OpenPlaylist("B").Wait();

        Assert.Equal("B", tc.View.OpenPlaylist);
        Assert.Equal(Transport.Playing, tc.View.Transport);
        Assert.Equal(a2, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(new[] { a2, b0, b1 }, Queued(tc)); // row 2 passes out, at the top
        Assert.Equal(new Adjacency(tc.TrackOf(a2), tc.TrackOf(b0)).Name, tc.View.NextAdjacency!.Name);

        tc.Output.Advance(At(2.0)); // 2.5 s: inside the authored crossfade from 2 s
        Assert.Equal(b0, tc.View.Incoming?.Row.Track.FilePath);

        tc.Output.Advance(At(1.0)); // 3.5 s: the fade is over, a2 is silent
        Assert.Equal(b0, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(new[] { b0, b1 }, Queued(tc)); // and its row is gone
        Assert.Equal(new[] { b0, b1 }, Entries(tc, "B"));
        Assert.Equal(new[] { a0, a1, a2 }, Entries(tc, "A"));
    }

    [Fact]
    public void ThePassingRow_IsNeverWritten_EvenWhenTheNewPlaylistIsEdited()
    {
        using var tc = TestEngine.Create();
        var (a0, b0, b1, b2) = (Tone("pass-a0"), Tone("pass-b0"), Tone("pass-b1"), Tone("pass-b2"));
        WritePlaylist(tc, "A", a0);
        WritePlaylist(tc, "B", b0, b1);
        tc.Engine.OpenPlaylist("A").Wait();
        tc.Engine.Play();
        tc.Output.Advance(At(0.5));
        tc.Engine.OpenPlaylist("B").Wait();

        tc.Add(b2);
        tc.Engine.Move([tc.RowOf(b1)], tc.RowOf(a0)); // dropped above the passing row: to the top of B

        Assert.Equal(new[] { a0, b1, b0, b2 }, Queued(tc));
        Assert.Equal(new[] { b1, b0, b2 }, Entries(tc, "B"));
    }

    [Fact]
    public void OpenPlaylist_ThatIsNotThere_IsANotice_AndNothingChanges()
    {
        using var tc = TestEngine.Create();
        var a = Tone("missing-a");
        tc.Add(a);

        tc.Engine.OpenPlaylist("Nowhere").Wait();

        Assert.Equal("Untitled", tc.View.OpenPlaylist);
        Assert.Equal(new[] { a }, Queued(tc));
        Assert.Equal("There is no playlist called Nowhere.", Assert.Single(tc.Notices));
    }

    [Fact]
    public void AMissingEntry_IsRejectedAsAddRejectsIt_AndTheFileIsLeftAsItIs()
    {
        using var tc = TestEngine.Create();
        var (b0, b1) = (Tone("gap-b0"), Tone("gap-b1"));
        var gone = Path.Combine(tc.TempDir, "gone.wav");
        WritePlaylist(tc, "B", b0, gone, b1);

        var report = tc.Engine.OpenPlaylist("B").Result;

        Assert.Equal(new[] { b0, b1 }, Queued(tc));
        Assert.Equal(gone, Assert.Single(report.Rejected).Path);
        Assert.Contains("gone.wav", Assert.Single(tc.Notices));
        Assert.Equal(new[] { b0, gone, b1 }, Entries(tc, "B")); // opening writes nothing
    }

    // ── New, rename, delete ──────────────────────────────────────────────────────────────

    [Fact]
    public void NewPlaylist_TakesTheFirstFreeUntitledNumber_AndAnEmptyOneIsAlreadyNew()
    {
        using var tc = TestEngine.Create();
        tc.Add(Tone("new-a"));
        WritePlaylist(tc, "Untitled 3");

        tc.Engine.NewPlaylist();
        Assert.Equal("Untitled 2", tc.View.OpenPlaylist);
        Assert.Empty(tc.View.Queue);

        tc.Engine.NewPlaylist(); // Untitled 2 is empty: it is the new playlist
        Assert.Equal("Untitled 2", tc.View.OpenPlaylist);
        Assert.Equal(new[] { "Untitled", "Untitled 2", "Untitled 3" }, tc.View.Playlists);
    }

    [Fact]
    public void AnEmptyUntitled_IsDeleted_WhenAnotherPlaylistOpens()
    {
        using var tc = TestEngine.Create();
        WritePlaylist(tc, "A", Tone("hyg-a"));
        tc.Engine.OpenPlaylist("A").Wait(); // leaves the launch's empty Untitled

        Assert.False(File.Exists(PlaylistPath(tc, "Untitled")));
        Assert.Equal(new[] { "A" }, tc.View.Playlists);
    }

    [Fact]
    public void RenameAToC_RenamesTheFile_AndAsOverridesAnswerToC()
    {
        using var tc = TestEngine.Create();
        var (a0, a1) = (Tone("ren-a0"), Tone("ren-a1"));
        WritePlaylist(tc, "A", a0, a1);
        tc.Engine.OpenPlaylist("A").Wait();
        tc.Store.UpsertOverride("A", a0, a1, new TransitionSpec { FadeOutDuration = TimeSpan.FromSeconds(2) });

        tc.Engine.RenamePlaylist("A", "C");

        Assert.False(File.Exists(PlaylistPath(tc, "A")));
        Assert.Equal(new[] { a0, a1 }, Entries(tc, "C"));
        Assert.Equal("C", tc.View.OpenPlaylist);
        Assert.Equal(new[] { "C" }, tc.View.Playlists);
        var reloaded = new JsonTransitionStore(tc.Library.TransitionsPath);
        Assert.Null(reloaded.GetOverride("A", a0, a1));
        Assert.Equal(TimeSpan.FromSeconds(2), reloaded.GetOverride("C", a0, a1)!.FadeOutDuration);
        Assert.Empty(tc.Notices);
    }

    [Fact]
    public void RenameTheOpenPlaylist_ThenEdit_WritesUnderTheNewName_AndTheNextLaunchOpensIt()
    {
        using var tc = TestEngine.Create();
        var (a, b) = (Tone("renedit-a"), Tone("renedit-b"));
        tc.Add(a);

        tc.Engine.RenamePlaylist("Untitled", "Road trip");
        tc.Add(b);

        Assert.Equal(new[] { a, b }, Entries(tc, "Road trip"));
        Assert.False(File.Exists(PlaylistPath(tc, "Untitled")));
        Assert.Equal("Road trip", new SettingsStore(tc.SettingsPath).Settings.LastPlaylist);
    }

    [Fact]
    public void ACaseOnlyRename_Renames()
    {
        using var tc = TestEngine.Create();
        tc.Add(Tone("case-a"));

        tc.Engine.RenamePlaylist("untitled", "UNTITLED");

        Assert.Equal("UNTITLED", tc.View.OpenPlaylist);
        Assert.Equal(new[] { "UNTITLED" }, tc.View.Playlists);
    }

    [Theory]
    [InlineData("a/b", "cannot name a playlist: a file name cannot contain /")]
    [InlineData("what?", "cannot name a playlist: a file name cannot contain ?")]
    [InlineData("  ", "A playlist needs a name.")]
    [InlineData("ends.", "cannot end with a full stop")]
    [InlineData("CON", "Windows reserves that name")]
    [InlineData("b", "There is already a playlist called B.")]
    public void RenameToANameThatCannotBe_IsRefusedWithANotice_AndNothingChanges(string newName, string why)
    {
        using var tc = TestEngine.Create();
        var a = Tone("bad-a");
        WritePlaylist(tc, "A", a);
        WritePlaylist(tc, "B");
        tc.Engine.OpenPlaylist("A").Wait();

        tc.Engine.RenamePlaylist("A", newName);

        var notice = Assert.Single(tc.Notices);
        Assert.StartsWith("Not renamed: A.", notice);
        Assert.Contains(why, notice);
        Assert.Equal("A", tc.View.OpenPlaylist);
        Assert.Equal(new[] { a }, Entries(tc, "A"));
    }

    [Fact]
    public void DeleteTheOpenPlaylist_WhilePlaying_PlaybackGoesOn_AndTheFirstRemainingOpens()
    {
        using var tc = TestEngine.Create();
        var (a0, a1, b0, c0) = (Tone("del-a0"), Tone("del-a1"), Tone("del-b0"), Tone("del-c0"));
        WritePlaylist(tc, "C", c0);
        WritePlaylist(tc, "B", b0);
        WritePlaylist(tc, "A", a0, a1);
        tc.Engine.OpenPlaylist("A").Wait();
        tc.Store.UpsertOverride("A", a0, a1, new TransitionSpec { FadeOutDuration = TimeSpan.FromSeconds(2) });
        tc.Engine.Play();
        tc.Output.Advance(At(0.5));

        tc.Engine.DeletePlaylist("A");

        Assert.False(File.Exists(PlaylistPath(tc, "A")));
        Assert.Null(new JsonTransitionStore(tc.Library.TransitionsPath).GetOverride("A", a0, a1));
        Assert.Equal("B", tc.View.OpenPlaylist);
        Assert.Equal(new[] { "B", "C" }, tc.View.Playlists);
        Assert.Equal(Transport.Playing, tc.View.Transport);
        Assert.Equal(a0, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(new[] { a0, b0 }, Queued(tc));
        Assert.Empty(tc.Notices);
    }

    [Fact]
    public void DeleteTheLastPlaylist_OpensAFreshUntitled()
    {
        using var tc = TestEngine.Create();
        tc.Add(Tone("last-a"));

        tc.Engine.DeletePlaylist("Untitled");

        Assert.Equal("Untitled", tc.View.OpenPlaylist);
        Assert.Empty(tc.View.Queue);
        Assert.Empty(Entries(tc, "Untitled"));
    }

    [Fact]
    public void DeleteAPlaylistThatIsNotOpen_LeavesTheQueueAlone()
    {
        using var tc = TestEngine.Create();
        var a = Tone("other-a");
        tc.Add(a);
        WritePlaylist(tc, "B", Tone("other-b"));

        tc.Engine.DeletePlaylist("B");

        Assert.Equal("Untitled", tc.View.OpenPlaylist);
        Assert.Equal(new[] { a }, Queued(tc));
        Assert.Equal(new[] { "Untitled" }, tc.View.Playlists);
    }

    [Fact]
    public void WithNoLibrary_PlaylistCommandsAreRefused_AndAddStillQueues()
    {
        var sink = new RecordingSink();
        using var engine = new PlaybackEngine(new FakeAudioOutput(), (Library?)null, sink) { StartAddWorker = work => work() };
        var a = Tone("nolib-a");

        engine.Add([a]).Wait();
        engine.NewPlaylist();

        Assert.Null(engine.View.OpenPlaylist);
        Assert.Empty(engine.View.Playlists);
        Assert.Equal(new[] { a }, engine.View.Queue.Select(r => r.Track.FilePath));
        Assert.Equal("No new playlist. No library is open.", Assert.Single(sink.Notices));
    }
}

/// <summary>Playlists issue 01 across launches (D15, D20): the library's last open playlist opens
/// with row 0 attended at 0:00, and an empty Untitled does not outlive a quit.</summary>
public sealed class PlaylistsAcrossLaunchesTests : IDisposable
{
    private readonly string _root = Path.Combine(TestTemp.Root, "pl01", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Folder(string name)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>A launch on <paramref name="folder"/>: its playlist is loaded before this returns.</summary>
    private static (PlaybackEngine Engine, RecordingSink Sink) Launch(string folder)
    {
        var sink = new RecordingSink();
        var engine = new PlaybackEngine(new FakeAudioOutput(), Library.Open(folder), sink);
        engine.Launched.Wait(); // the launch's load runs on the engine's own worker
        engine.StartAddWorker = work => work();
        return (engine, sink);
    }

    private static string Tone(string name) => TestAudio.WriteToneWav($"pl01-launch-{name}.wav", 2, 1000);

    [Fact]
    public void TheLastOpenPlaylist_OpensAtLaunch_RowZeroAttendedAtTheStart()
    {
        var folder = Folder("Music");
        var (a, b) = (Tone("a"), Tone("b"));
        var (first, _) = Launch(folder);
        using (first)
        {
            first.Add([a, b]).Wait();
            first.RenamePlaylist("Untitled", "Evening");
        }

        var (engine, sink) = Launch(folder);
        using var _ = engine;

        Assert.Equal("Evening", engine.View.OpenPlaylist);
        Assert.Equal(new[] { a, b }, engine.View.Queue.Select(r => r.Track.FilePath));
        Assert.True(engine.View.Queue[0].IsCurrent);
        Assert.Equal(Transport.Stopped, engine.View.Transport);
        Assert.Empty(sink.Notices);
    }

    [Fact]
    public void QuitWithAnEmptyUntitled3Open_TheFileIsGoneOnRelaunch()
    {
        var folder = Folder("Music");
        var (first, _) = Launch(folder);
        using (first)
        {
            first.Add([Tone("u1")]).Wait();
            first.NewPlaylist();
            first.Add([Tone("u2")]).Wait();
            first.NewPlaylist();
            Assert.Equal("Untitled 3", first.View.OpenPlaylist);
        }
        var playlists = Path.Combine(folder, ".faderio", "playlists");
        Assert.False(File.Exists(Path.Combine(playlists, "Untitled 3.m3u8")));

        var (engine, _) = Launch(folder);
        using var _ = engine;

        Assert.Equal("Untitled 2", engine.View.OpenPlaylist); // the last one worth reopening
        Assert.Equal(new[] { "Untitled", "Untitled 2" }, engine.View.Playlists);
    }

    [Fact]
    public void ALastPlaylistThatIsGone_OpensAnUntitled()
    {
        var folder = Folder("Music");
        var library = Library.Open(folder);
        library.Settings!.Save(new PlayerSettings { LastPlaylist = "Gone" });

        var (engine, _) = Launch(folder);
        using var _ = engine;

        Assert.Equal("Untitled", engine.View.OpenPlaylist);
        Assert.True(File.Exists(Path.Combine(folder, ".faderio", "playlists", "Untitled.m3u8")));
    }

    [Fact]
    public void OpenLibrary_OpensEachLibrarysOwnLastPlaylist()
    {
        var (music, other) = (Folder("Music"), Folder("Other"));
        var a = Tone("lib-a");
        var (engine, _) = Launch(music);
        using var _ = engine;
        engine.Add([a]).Wait();

        engine.OpenLibrary(other);
        Assert.Equal("Untitled", engine.View.OpenPlaylist);
        Assert.Empty(engine.View.Queue);

        engine.OpenLibrary(music);
        Assert.Equal("Untitled", engine.View.OpenPlaylist);
        Assert.Equal(new[] { a }, engine.View.Queue.Select(r => r.Track.FilePath));
        Assert.False(File.Exists(Path.Combine(other, ".faderio", "playlists", "Untitled.m3u8"))); // empty, left
    }
}
