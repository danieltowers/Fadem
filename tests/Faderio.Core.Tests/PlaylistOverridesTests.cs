using System.Text;
using System.Text.Json;
using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Settings;

namespace Faderio.Core.Tests;

/// <summary>
/// Playlists issue 04 (D21–D23): the engine passes the open playlist's override to the resolver
/// wherever it resolves an adjacency, and authors it through <c>AuthorOverride</c> and
/// <c>UnauthorOverride</c>. Each acceptance line renders through the chain on the fake output and
/// is read by where the fade begins. The outgoing track is DC at 0.4 and the incoming at 0.1,
/// with linear fades, so the output leaves 0.4 at the first frame of the fade, or of a hard cut.
/// </summary>
public sealed class PlaylistOverridesTests
{
    private const int Rate = 44100;

    private static int At(double seconds) => (int)Math.Round(seconds * Rate);

    private static TimeSpan Seconds(double s) => TimeSpan.FromSeconds(s);

    private static (string A, string B) Pair(string name) =>
        (TestAudio.WriteDcWav($"ovr-{name}-out.wav", 3, 0.4), TestAudio.WriteDcWav($"ovr-{name}-in.wav", 3, 0.1));

    private static void WritePlaylist(TestEngine tc, string name, params string[] paths) =>
        File.WriteAllLines(Path.Combine(tc.Library.PlaylistsFolder, name + ".m3u8"), ["#EXTM3U", .. paths], new UTF8Encoding(false));

    /// <summary>A → B fading out at <paramref name="outPoint"/>, 0.5 s each way, linear.</summary>
    private static TransitionSpec Crossfade(double outPoint) => new()
    {
        FadeOutPoint = new FadePointSpec(Seconds(outPoint), FromEnd: false),
        FadeOutDuration = Seconds(0.5),
        FadeInPoint = TimeSpan.Zero,
        FadeInDuration = Seconds(0.5),
        Curve = FadeCurveKind.Linear,
    };

    private static TransitionSpec FadeOutAt(double outPoint) =>
        new() { FadeOutPoint = new FadePointSpec(Seconds(outPoint), FromEnd: false) };

    /// <summary>Two playlists, P and Q, each holding A then B, with P open.</summary>
    private static (TestEngine Tc, string A, string B) InPAndQ(string name)
    {
        var tc = TestEngine.Create();
        var (a, b) = Pair(name);
        WritePlaylist(tc, "P", a, b);
        WritePlaylist(tc, "Q", a, b);
        tc.Engine.OpenPlaylist("P").Wait();
        return (tc, a, b);
    }

    /// <summary>Opens <paramref name="playlist"/> with nothing sounding, plays A from its start,
    /// and returns where the output first leaves A's level, in seconds from A's start.</summary>
    private static double FadeStartIn(TestEngine tc, string playlist, string a)
    {
        tc.Engine.Stop();
        tc.Engine.OpenPlaylist(playlist).Wait();
        Assert.Equal(playlist, tc.View.OpenPlaylist);
        var from = tc.Output.Blocks.Count;
        tc.Engine.PlayRow(tc.RowOf(a));
        tc.Output.Advance(At(3.5));
        var left = tc.Output.Blocks.Skip(from).SelectMany(block => block).Where((_, i) => i % 2 == 0).ToArray();
        var level = left[0];
        var frame = Array.FindIndex(left, s => Math.Abs(s - level) > 1e-5);
        Assert.True(frame >= 0, "the output never left A's level");
        return (double)frame / Rate;
    }

    // ── Acceptance, through the chain ────────────────────────────────────────────────────

    [Fact]
    public void AnOverriddenFadeOutPoint_PlaysInItsPlaylist_AndTheGlobalPlaysInAnother()
    {
        var (tc, a, b) = InPAndQ("point");
        using var _ = tc;
        var adjacency = tc.AdjacencyOf(a, b);
        tc.Engine.Author(adjacency, Crossfade(2));

        Assert.Empty(tc.Engine.AuthorOverride(adjacency, FadeOutAt(1.5)));

        Assert.Equal(1.5, FadeStartIn(tc, "P", a), 3);
        Assert.Equal(2.0, FadeStartIn(tc, "Q", a), 3);
    }

    [Fact]
    public void ARender_PlaysTheOverrideInItsPlaylist_AndTheGlobalInAnother()
    {
        var (tc, a, b) = InPAndQ("render");
        using var _ = tc;
        var adjacency = tc.AdjacencyOf(a, b);
        tc.Engine.Author(adjacency, Crossfade(2));
        Assert.Empty(tc.Engine.AuthorOverride(adjacency, FadeOutAt(1.5)));

        var inP = tc.Engine.RenderBoundary(adjacency, Path.Combine(tc.TempDir, "p.wav"));
        tc.Engine.OpenPlaylist("Q").Wait();
        var inQ = tc.Engine.RenderBoundary(adjacency, Path.Combine(tc.TempDir, "q.wav"));

        // The pre-roll reaches back to A's start, so the fade-out point is where it sits in A.
        Assert.Equal(1500, inP.FadeOutPointInFile.Milliseconds);
        Assert.Equal(2000, inQ.FadeOutPointInFile.Milliseconds);
        Assert.Equal(Crossfade(2), tc.Store.Get(a, b)); // the render wrote nothing
        Assert.Equal(FadeOutAt(1.5), tc.Store.GetOverride("P", a, b));
    }

    [Fact]
    public void APlaylistOnlyTransition_RendersInItsPlaylist_AndAsUnauthoredElsewhere()
    {
        var (tc, a, b) = InPAndQ("render-only");
        using var _ = tc;
        var adjacency = tc.AdjacencyOf(a, b);
        Assert.Empty(tc.Engine.AuthorOverride(adjacency, Crossfade(1.5)));

        var inP = tc.Engine.RenderBoundary(adjacency, Path.Combine(tc.TempDir, "p.wav"));
        tc.Engine.OpenPlaylist("Q").Wait();
        var inQ = tc.Engine.RenderBoundary(adjacency, Path.Combine(tc.TempDir, "q.wav"));

        Assert.Equal(1500, inP.FadeOutPointInFile.Milliseconds);
        Assert.Equal(3000, inQ.FadeOutPointInFile.Milliseconds); // a hard cut at A's end
    }

    [Fact]
    public void AnOverrideOfTheFadeInDurationOnly_LeavesTheRestToTheGlobal_AndFollowsItsLaterEdits()
    {
        var (tc, a, b) = InPAndQ("follow");
        using var _ = tc;
        var adjacency = tc.AdjacencyOf(a, b);
        tc.Engine.Author(adjacency, Crossfade(2));
        tc.Engine.AuthorOverride(adjacency, new TransitionSpec { FadeInDuration = Seconds(1) });
        Assert.Equal(2.0, FadeStartIn(tc, "P", a), 3);

        tc.Engine.Author(adjacency, Crossfade(1.25)); // a later edit to the global

        Assert.Equal(1.25, FadeStartIn(tc, "P", a), 3);
        var layers = tc.Engine.Transition(adjacency);
        Assert.Equal(Crossfade(1.25) with { FadeInDuration = Seconds(1) }, layers.Effective);
        Assert.Equal(new TransitionSpec { FadeInDuration = Seconds(1) }, layers.Override);
    }

    [Fact]
    public void AnOverrideWithNoGlobal_IsAPlaylistOnlyTransition_AndAnotherPlaylistPlaysTheUnauthoredFallback()
    {
        var (tc, a, b) = InPAndQ("only");
        using var _ = tc;
        var adjacency = tc.AdjacencyOf(a, b);

        Assert.Empty(tc.Engine.AuthorOverride(adjacency, Crossfade(1.5)));

        Assert.Equal(1.5, FadeStartIn(tc, "P", a), 3);
        Assert.Equal(3.0, FadeStartIn(tc, "Q", a), 3); // a hard cut at A's end
        Assert.Null(tc.Engine.Transition(adjacency).Global);
    }

    [Fact]
    public void AnOverrideWithNoGlobal_InAnotherPlaylist_PlaysTheDefaultCrossfadeWhenOptedIn()
    {
        var tc = TestEngine.Create(new PlayerSettings
        {
            UnauthoredAdjacency = UnauthoredAdjacency.DefaultCrossfade,
            DefaultCrossfadeSeconds = 1,
        });
        using var _ = tc;
        var (a, b) = Pair("default");
        WritePlaylist(tc, "P", a, b);
        WritePlaylist(tc, "Q", a, b);
        tc.Engine.OpenPlaylist("P").Wait();
        tc.Engine.AuthorOverride(tc.AdjacencyOf(a, b), Crossfade(1.5));

        Assert.Equal(1.5, FadeStartIn(tc, "P", a), 3);
        Assert.Equal(2.0, FadeStartIn(tc, "Q", a), 2); // one second before A's end
    }

    [Fact]
    public void RenamingThePlaylist_TakesItsOverrideWithIt()
    {
        var (tc, a, b) = InPAndQ("rename");
        using var _ = tc;
        var adjacency = tc.AdjacencyOf(a, b);
        tc.Engine.Author(adjacency, Crossfade(2));
        tc.Engine.AuthorOverride(adjacency, FadeOutAt(1.5));

        tc.Engine.RenamePlaylist("P", "R");

        Assert.Equal(FadeOutAt(1.5), tc.Engine.Transition(adjacency).Override);
        Assert.Equal(1.5, FadeStartIn(tc, "R", a), 3);
    }

    [Fact]
    public void RemovingTheOverride_PlaysTheGlobalAgain()
    {
        var (tc, a, b) = InPAndQ("remove");
        using var _ = tc;
        var adjacency = tc.AdjacencyOf(a, b);
        tc.Engine.Author(adjacency, Crossfade(2));
        tc.Engine.AuthorOverride(adjacency, FadeOutAt(1.5));

        tc.Engine.UnauthorOverride(adjacency);

        Assert.Contains("Removed the override of " + adjacency.Name + " in P.", tc.Notices);
        Assert.Null(tc.Engine.Transition(adjacency).Override);
        Assert.Equal(2.0, FadeStartIn(tc, "P", a), 3);
    }

    // ── Resolution everywhere ────────────────────────────────────────────────────────────

    [Fact]
    public void AnOverrideSavedWhileItsBoundaryIsPending_ReschedulesIt()
    {
        var (tc, a, b) = InPAndQ("pending");
        using var _ = tc;
        var adjacency = tc.AdjacencyOf(a, b);
        tc.Engine.Author(adjacency, Crossfade(2));
        tc.Engine.PlayRow(tc.RowOf(a));
        tc.Output.Advance(At(0.5));

        tc.Engine.AuthorOverride(adjacency, FadeOutAt(1.5));
        tc.Output.Advance(At(1.2)); // to 1.7 s: past the override's point, before the global's

        Assert.Equal(b, tc.View.Incoming?.Row.Track.FilePath);
    }

    [Fact]
    public void TransitionNow_PlaysTheOverride_ButForItsFadeOutPoint()
    {
        var (tc, a, b) = InPAndQ("now");
        using var _ = tc;
        var adjacency = tc.AdjacencyOf(a, b);
        tc.Engine.Author(adjacency, Crossfade(2));
        tc.Engine.AuthorOverride(adjacency, new TransitionSpec { FadeOutDuration = Seconds(2) });
        tc.Engine.PlayRow(tc.RowOf(a));
        tc.Output.Advance(At(0.5));

        tc.Engine.TransitionNow();
        tc.Output.Advance(At(1.0)); // 1 s into a 2 s fade-out: the global's 0.5 s would be over

        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(b, tc.View.Incoming?.Row.Track.FilePath);
    }

    [Fact]
    public void APreviewOfADraftedOverride_PlaysItOverTheGlobal()
    {
        var (tc, a, b) = InPAndQ("preview");
        using var _ = tc;
        var adjacency = tc.AdjacencyOf(a, b);
        tc.Engine.Author(adjacency, Crossfade(2));

        Assert.Empty(tc.Engine.Preview(adjacency, FadeOutAt(1.5), TransitionLayer.Playlist));
        tc.Output.Advance(At(1.4)); // the preview starts at A's start: its point is under 5 s in
        Assert.Null(tc.View.Preview!.Incoming);
        tc.Output.Advance(At(0.2));

        Assert.Equal(PreviewPhase.Transition, tc.View.Preview!.Phase);
        Assert.Null(tc.Engine.Transition(adjacency).Override); // nothing reached the store
    }

    [Fact]
    public void APreviewOfAGlobalDraft_PlaysUnderTheStoredOverride()
    {
        var (tc, a, b) = InPAndQ("preview-global");
        using var _ = tc;
        var adjacency = tc.AdjacencyOf(a, b);
        tc.Engine.AuthorOverride(adjacency, FadeOutAt(1.5));

        tc.Engine.Preview(adjacency, Crossfade(2));
        tc.Output.Advance(At(1.6));

        Assert.Equal(PreviewPhase.Transition, tc.View.Preview!.Phase);
    }

    [Fact]
    public void TheRowMark_CountsAPlaylistOnlyTransition_InItsPlaylistAlone()
    {
        var (tc, a, b) = InPAndQ("mark");
        using var _ = tc;
        tc.Engine.AuthorOverride(tc.AdjacencyOf(a, b), Crossfade(1.5));

        Assert.True(tc.View.Queue[0].NextAuthored);
        tc.Engine.OpenPlaylist("Q").Wait();
        Assert.False(tc.View.Queue[0].NextAuthored);
    }

    // ── The authoring surface ────────────────────────────────────────────────────────────

    [Fact]
    public void Transition_GivesBothLayers_AndWhetherThePlaylistPlaysSomethingElse()
    {
        var (tc, a, b) = InPAndQ("layers");
        using var _ = tc;
        var adjacency = tc.AdjacencyOf(a, b);
        tc.Engine.Author(adjacency, Crossfade(2));
        Assert.False(tc.Engine.Transition(adjacency).Differ);

        tc.Engine.AuthorOverride(adjacency, FadeOutAt(1.5) with { Curve = FadeCurveKind.EqualPower });

        var layers = tc.Engine.Transition(adjacency);
        Assert.Equal(Crossfade(2), layers.Global);
        Assert.Equal(FadeOutAt(1.5) with { Curve = FadeCurveKind.EqualPower }, layers.Override);
        Assert.Equal(Crossfade(1.5) with { Curve = FadeCurveKind.EqualPower }, layers.Effective);
        Assert.True(layers.Differ);
    }

    [Fact]
    public void ABlankDuration_PlaysTheGlobalsInThePlaylistLayer_AndTheBlankDurationOtherwise()
    {
        var global = Crossfade(1) with { FadeOutDuration = Seconds(2), FadeInDuration = Seconds(3) };
        var withDurations = new TransitionLayers(global, null, global);
        var blankGlobal = new TransitionLayers(FadeOutAt(1.5), null, FadeOutAt(1.5));

        Assert.Equal(Seconds(2), withDurations.BlankFadeOutDuration(TransitionLayer.Playlist));
        Assert.Equal(Seconds(3), withDurations.BlankFadeInDuration(TransitionLayer.Playlist));
        // The global layer's own blank never falls through to anything but the blank duration,
        // five seconds where the engine gave none.
        Assert.Equal(Seconds(5), withDurations.BlankFadeOutDuration(TransitionLayer.Global));
        Assert.Equal(Seconds(5), withDurations.BlankFadeInDuration(TransitionLayer.Global));
        Assert.Equal(Seconds(5), blankGlobal.BlankFadeOutDuration(TransitionLayer.Playlist));
        Assert.Equal(Seconds(5), TransitionLayers.None.BlankFadeInDuration(TransitionLayer.Playlist));
        var eight = blankGlobal with { BlankDuration = Seconds(8) };
        Assert.Equal(Seconds(8), eight.BlankFadeOutDuration(TransitionLayer.Playlist));
        Assert.Equal(Seconds(8), eight.BlankFadeInDuration(TransitionLayer.Global));
    }

    [Fact]
    public void AnOverride_IsValidatedAgainstTheEffectiveTransition_AndARefusedOneIsNotStored()
    {
        var (tc, a, b) = InPAndQ("refused");
        using var _ = tc;
        var adjacency = tc.AdjacencyOf(a, b);
        tc.Engine.Author(adjacency, Crossfade(2));

        var refusals = tc.Engine.AuthorOverride(adjacency, new TransitionSpec
        {
            FadeInPoint = Seconds(4), // past the end of the 3 s incoming track
            FadeOutDuration = Seconds(-1),
        });

        Assert.Equal(new[] { TransitionField.FadeOutDuration, TransitionField.FadeInPoint }, refusals.Select(r => r.Field));
        Assert.Null(tc.Engine.Transition(adjacency).Override);
        Assert.StartsWith($"Not saved: {adjacency.Name} in P. ", tc.Notices[^1]);
    }

    [Fact]
    public void AnOverrideThatSetsNothing_RemovesTheOverride()
    {
        var (tc, a, b) = InPAndQ("nothing");
        using var _ = tc;
        var adjacency = tc.AdjacencyOf(a, b);
        tc.Engine.AuthorOverride(adjacency, Crossfade(1.5));

        tc.Engine.AuthorOverride(adjacency, new TransitionSpec());

        Assert.Null(tc.Engine.Transition(adjacency).Override);
        tc.Engine.AuthorOverride(adjacency, new TransitionSpec { Curve = FadeCurveKind.Linear });
        Assert.Null(tc.Engine.Transition(adjacency).Override); // a curve alone, with no global, places no fade
        Assert.Equal($"Nothing to save: {adjacency.Name} in P overrides nothing.", tc.Notices[^1]);
    }

    [Fact]
    public void ACurveAloneOverridesAGlobal()
    {
        var (tc, a, b) = InPAndQ("curve");
        using var _ = tc;
        var adjacency = tc.AdjacencyOf(a, b);
        tc.Engine.Author(adjacency, Crossfade(2));

        tc.Engine.AuthorOverride(adjacency, new TransitionSpec { Curve = FadeCurveKind.EqualPower });

        Assert.Equal(FadeCurveKind.EqualPower, tc.Engine.Transition(adjacency).Effective!.Curve);
    }

    [Fact]
    public void APartialOverride_IsWrittenWithOnlyTheParametersItSets()
    {
        var (tc, a, b) = InPAndQ("json");
        using var _ = tc;
        tc.Engine.Author(tc.AdjacencyOf(a, b), Crossfade(2));

        tc.Engine.AuthorOverride(tc.AdjacencyOf(a, b), new TransitionSpec { FadeInDuration = Seconds(1) });

        using var json = JsonDocument.Parse(File.ReadAllText(tc.Library.TransitionsPath));
        var byPair = json.RootElement.GetProperty("playlistOverrides").GetProperty("P");
        var written = byPair.EnumerateObject().Single().Value;
        Assert.Equal(new[] { "FadeInDuration" }, written.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void IsAuthored_InThePlaylistLayer_ComparesTheOverride_WithAMissingCurveAsTheGlobals()
    {
        var (tc, a, b) = InPAndQ("isauthored");
        using var _ = tc;
        var adjacency = tc.AdjacencyOf(a, b);
        tc.Engine.Author(adjacency, Crossfade(2));
        Assert.True(tc.Engine.IsAuthored(adjacency, new TransitionSpec(), TransitionLayer.Playlist));
        Assert.False(tc.Engine.IsAuthored(adjacency, FadeOutAt(1.5), TransitionLayer.Playlist));

        tc.Engine.AuthorOverride(adjacency, FadeOutAt(1.5));

        Assert.True(tc.Engine.IsAuthored(adjacency, FadeOutAt(1.5), TransitionLayer.Playlist));
        Assert.False(tc.Engine.IsAuthored(adjacency, FadeOutAt(1.5) with { Curve = FadeCurveKind.Linear }, TransitionLayer.Playlist));
        Assert.True(tc.Engine.IsAuthored(adjacency, Crossfade(2))); // the global layer is as it was
    }

    [Fact]
    public void WithNoLibrary_AnOverrideIsRefusedWithANotice()
    {
        var tc = TestEngine.Create();
        using var _ = tc;
        var (a, b) = Pair("nolib");
        var adjacency = new Adjacency(tc.TrackOf(a), tc.TrackOf(b));
        using var engine = new PlaybackEngine(new FakeAudioOutput(), (Faderio.Core.Transitions.ITransitionStore?)null, tc.Sink);

        engine.AuthorOverride(adjacency, FadeOutAt(1.5));
        engine.UnauthorOverride(adjacency);

        Assert.Equal($"Not saved: {adjacency.Name}. No library is open.", tc.Notices[^2]);
        Assert.Equal($"Not removed: {adjacency.Name}. No library is open.", tc.Notices[^1]);
        Assert.Equal(TransitionLayers.None, engine.Transition(adjacency));
    }
}
