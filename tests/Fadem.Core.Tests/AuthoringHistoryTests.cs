using System.Text;
using Fadem.Core.Domain;
using Fadem.Core.Playback;

namespace Fadem.Core.Tests;

/// <summary>Phase 6 issue 04: one history of this session's transition changes, stepped back by
/// <c>Undo</c> and forth by <c>Redo</c>. Each step puts one layer of one adjacency back as it
/// was, and reschedules as a save does.</summary>
public sealed class AuthoringHistoryTests
{
    private const int Second = 44100;
    private const double FA = 1000;
    private const double FB = 2000;

    private static TransitionSpec CrossfadeAt(double outPoint, double duration = 1.0) => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(outPoint), FromEnd: false),
        FadeInPoint = TimeSpan.Zero,
        FadeOutDuration = TimeSpan.FromSeconds(duration),
        FadeInDuration = TimeSpan.FromSeconds(duration),
    };

    private static (TestEngine Tc, Adjacency AB) Pair(string name)
    {
        var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav($"hist-{name}-a.wav", 3, FA);
        var b = TestAudio.WriteToneWav($"hist-{name}-b.wav", 3, FB);
        tc.Add(a, b);
        return (tc, tc.AdjacencyOf(a, b));
    }

    private static TransitionSpec? Stored(TestEngine tc, Adjacency adjacency) =>
        tc.Store.Get(adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath);

    [Fact]
    public void ASave_IsUndone_ThenRedone()
    {
        var (tc, ab) = Pair("save");
        using var _ = tc;
        tc.Engine.Author(ab, CrossfadeAt(1));
        tc.Engine.Author(ab, CrossfadeAt(2));

        Assert.Equal(ab, tc.Engine.Undo());
        Assert.Equal(CrossfadeAt(1), Stored(tc, ab));
        Assert.Equal(new HistoryStep(ab, null, Removed: false), tc.View.NextRedo);
        Assert.Equal(new HistoryStep(ab, null, Removed: false), tc.View.NextUndo);

        Assert.Equal(ab, tc.Engine.Redo());
        Assert.Equal(CrossfadeAt(2), Stored(tc, ab));
        Assert.Null(tc.View.NextRedo);
    }

    [Fact]
    public void TheFirstSave_Undone_LeavesNothingStored_AndNothingMoreToUndo()
    {
        var (tc, ab) = Pair("first");
        using var _ = tc;
        tc.Engine.Author(ab, CrossfadeAt(1));

        tc.Engine.Undo();

        Assert.Null(Stored(tc, ab));
        Assert.Null(tc.View.NextUndo);
        Assert.Null(tc.Engine.Undo());
    }

    [Fact]
    public void SavesOnTwoAdjacencies_AreUndoneNewestFirst_EachReturningItsOwn()
    {
        using var tc = TestEngine.Create();
        var p = Enumerable.Range(0, 3).Select(i => TestAudio.WriteToneWav($"hist-two-{i}.wav", 3, FA + i * 500)).ToArray();
        tc.Add(p);
        var (ab, bc) = (tc.AdjacencyOf(p[0], p[1]), tc.AdjacencyOf(p[1], p[2]));
        tc.Engine.Author(ab, CrossfadeAt(1));
        tc.Engine.Author(bc, CrossfadeAt(1));

        Assert.Equal(bc, tc.Engine.Undo());
        Assert.Null(Stored(tc, bc));
        Assert.NotNull(Stored(tc, ab));
        Assert.Equal(ab, tc.Engine.Undo());
        Assert.Null(Stored(tc, ab));
    }

    [Fact]
    public void RemoveTransition_IsUndone()
    {
        var (tc, ab) = Pair("remove");
        using var _ = tc;
        tc.Engine.Author(ab, CrossfadeAt(1));
        tc.Engine.Unauthor(ab);
        Assert.Equal(new HistoryStep(ab, null, Removed: true), tc.View.NextUndo);

        tc.Engine.Undo();

        Assert.Equal(CrossfadeAt(1), Stored(tc, ab));
    }

    [Fact]
    public void ABlankSave_Removes_AndIsUndoneAsARemoval()
    {
        var (tc, ab) = Pair("blank");
        using var _ = tc;
        tc.Engine.Author(ab, CrossfadeAt(1));
        tc.Engine.Author(ab, new TransitionSpec());

        tc.Engine.Undo();

        Assert.Equal(CrossfadeAt(1), Stored(tc, ab));
    }

    [Fact]
    public void AnOverride_IsUndone_ThenRedone_AndItsRemovalToo()
    {
        var (tc, ab) = Pair("override");
        using var _ = tc;
        var playlist = tc.View.OpenPlaylist!;
        tc.Engine.Author(ab, CrossfadeAt(1));
        var partial = new TransitionSpec { FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(2), FromEnd: false) };
        tc.Engine.AuthorOverride(ab, partial);
        tc.Engine.UnauthorOverride(ab);
        Assert.Equal(new HistoryStep(ab, playlist, Removed: true), tc.View.NextUndo);

        tc.Engine.Undo();
        Assert.Equal(partial, tc.Store.GetOverride(playlist, ab.Outgoing.FilePath, ab.Incoming.FilePath));
        tc.Engine.Undo();
        Assert.Null(tc.Store.GetOverride(playlist, ab.Outgoing.FilePath, ab.Incoming.FilePath));
        Assert.Equal(CrossfadeAt(1), Stored(tc, ab)); // the global untouched
        tc.Engine.Redo();
        Assert.Equal(partial, tc.Store.GetOverride(playlist, ab.Outgoing.FilePath, ab.Incoming.FilePath));
    }

    [Fact]
    public void AChangeAfterAnUndo_DropsWhatCouldBeRedone()
    {
        var (tc, ab) = Pair("drop");
        using var _ = tc;
        tc.Engine.Author(ab, CrossfadeAt(1));
        tc.Engine.Author(ab, CrossfadeAt(2));
        tc.Engine.Undo();

        tc.Engine.Author(ab, CrossfadeAt(1.5));

        Assert.Null(tc.View.NextRedo);
        Assert.Null(tc.Engine.Redo());
        Assert.Equal(CrossfadeAt(1.5), Stored(tc, ab));
    }

    [Fact]
    public void ARefusal_NothingToSave_AndTheSameValuesAgain_AreNoSteps()
    {
        var (tc, ab) = Pair("none");
        using var _ = tc;
        Assert.NotEmpty(tc.Engine.Author(ab, CrossfadeAt(99))); // past the track's end: refused
        tc.Engine.Author(ab, new TransitionSpec()); // nothing to save
        tc.Engine.Unauthor(ab); // nothing to remove
        Assert.Null(tc.View.NextUndo);

        tc.Engine.Author(ab, CrossfadeAt(1));
        tc.Engine.Author(ab, CrossfadeAt(1));
        tc.Engine.Undo();

        Assert.Null(tc.View.NextUndo);
        Assert.Null(Stored(tc, ab));
    }

    [Fact]
    public void UndoingARemoval_WhileTheBoundaryIsPending_PutsTheFadeBackIntoThisPlayback()
    {
        var (tc, ab) = Pair("pending");
        using var _ = tc;
        tc.Engine.Author(ab, CrossfadeAt(1));
        tc.Engine.Unauthor(ab); // A → B now a hard cut at A's end
        tc.Engine.Play();
        tc.Output.Advance(Second / 2);

        tc.Engine.Undo();

        tc.Output.Advance(Second); // 1.5 s: halfway through the crossfade
        Assert.Equal(ab.Incoming.FilePath, tc.View.Incoming!.Row.Track.FilePath);
        Assert.True(tc.Rendered.ToneAmplitude(FB, Second + Second / 2 - 2048) > 0.1, "B must be fading in");
    }

    [Fact]
    public void UndoingASave_AfterItsFadeStarted_LetsTheFadePlayOut()
    {
        var (tc, ab) = Pair("flight");
        using var _ = tc;
        tc.Engine.Author(ab, CrossfadeAt(1));
        tc.Engine.Play();
        tc.Output.Advance(Second + Second / 4); // 1.25 s: B is fading in under A

        tc.Engine.Undo();

        Assert.Null(Stored(tc, ab));
        Assert.Equal(ab.Incoming.FilePath, tc.View.Incoming!.Row.Track.FilePath);
        tc.Output.Advance(Second);
        Assert.Equal(ab.Incoming.FilePath, tc.View.Attended!.Row.Track.FilePath);
    }

    [Fact]
    public void Undo_AndRedo_SayWhatTheyDid()
    {
        var (tc, ab) = Pair("notices");
        using var _ = tc;
        tc.Engine.Author(ab, CrossfadeAt(1));
        tc.Engine.Unauthor(ab);

        tc.Engine.Undo();
        tc.Engine.Redo();

        Assert.Equal($"Undone: removed {ab.Name}.", tc.Notices[^2]);
        Assert.Equal($"Redone: removed {ab.Name}.", tc.Notices[^1]);
    }

    [Fact]
    public void OpeningAnotherLibrary_ClearsTheHistory()
    {
        var (tc, ab) = Pair("library");
        using var _ = tc;
        tc.Engine.Author(ab, CrossfadeAt(1));
        var other = Path.Combine(tc.TempDir, "other library");
        Directory.CreateDirectory(other);

        tc.Engine.OpenLibrary(other);

        Assert.Null(tc.View.NextUndo);
        Assert.Null(tc.Engine.Undo());
    }

    [Fact]
    public void ARenamedPlaylistsOverride_IsUndoneUnderItsNewName_AndADeletedOnesStepsGo()
    {
        var (tc, ab) = Pair("rename");
        using var _ = tc;
        File.WriteAllLines(Path.Combine(tc.Library.PlaylistsFolder, "P.m3u8"),
            ["#EXTM3U", ab.Outgoing.FilePath, ab.Incoming.FilePath], new UTF8Encoding(false));
        tc.Engine.OpenPlaylist("P").Wait();
        var partial = new TransitionSpec { FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(2), FromEnd: false) };
        tc.Engine.Author(ab, CrossfadeAt(1));
        tc.Engine.AuthorOverride(ab, partial);

        tc.Engine.RenamePlaylist("P", "R");
        Assert.Equal(new HistoryStep(ab, "R", Removed: false), tc.View.NextUndo);
        tc.Engine.Undo();
        Assert.Null(tc.Store.GetOverride("R", ab.Outgoing.FilePath, ab.Incoming.FilePath));
        tc.Engine.Redo();

        tc.Engine.DeletePlaylist("R");
        Assert.Equal(new HistoryStep(ab, null, Removed: false), tc.View.NextUndo); // the global save
    }
}
