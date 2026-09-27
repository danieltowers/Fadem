using System.Text;
using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Transitions;

namespace Faderio.Core.Tests;

/// <summary>
/// Playlists issue 05 (D24): the transition manager's two queries, <c>AdjacenciesInStore</c> and
/// <c>TracksInStore</c>, which read the store and probe files but decode nothing, and
/// <c>PreviewStored</c>, whose two layers are read by where each preview's pre-roll begins in the
/// outgoing track: 5 s before the transition it plays.
/// </summary>
public sealed class TransitionManagerTests
{
    private const int Second = 44100;

    private static TimeSpan Seconds(double s) => TimeSpan.FromSeconds(s);

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

    private static void WritePlaylist(TestEngine tc, string name, params string[] paths) =>
        File.WriteAllLines(Path.Combine(tc.Library.PlaylistsFolder, name + ".m3u8"), ["#EXTM3U", .. paths], new UTF8Encoding(false));

    /// <summary>Tracks a to e, named so their titles sort in that order, in playlists P and Q,
    /// with P open.</summary>
    private static (TestEngine Tc, string[] Paths) FiveInPAndQ(double seconds = 3)
    {
        var tc = TestEngine.Create();
        var paths = new[] { "a", "b", "c", "d", "e" }
            .Select(n => TestAudio.WriteDcWav($"mgr-{n}.wav", seconds, 0.2))
            .ToArray();
        WritePlaylist(tc, "P", paths);
        WritePlaylist(tc, "Q", paths);
        tc.Engine.OpenPlaylist("P").Wait();
        return (tc, paths);
    }

    private static Track TrackAt(TestEngine tc, string path) => tc.View.Queue.First(r => r.Track.FilePath == path).Track;

    // ── AdjacenciesInStore ───────────────────────────────────────────────────────────────

    [Fact]
    public void EveryAdjacencyOfTheTrack_IsListed_OutgoingFirst_ThenIncoming_EachByTheOtherTrack()
    {
        var (tc, p) = FiveInPAndQ();
        using var _ = tc;
        var (a, b, c, d, e) = (p[0], p[1], p[2], p[3], p[4]);
        tc.Engine.Author(tc.AdjacencyOf(a, c), Crossfade(2));
        tc.Engine.Author(tc.AdjacencyOf(a, b), Crossfade(2));
        tc.Engine.AuthorOverride(tc.AdjacencyOf(a, b), FadeOutAt(1.5));
        tc.Engine.Author(tc.AdjacencyOf(e, a), Crossfade(2));
        tc.Engine.Author(tc.AdjacencyOf(d, a), Crossfade(2));
        tc.Engine.Author(tc.AdjacencyOf(b, c), Crossfade(2)); // not a's

        var listed = tc.Engine.AdjacenciesInStore(TrackAt(tc, a));

        Assert.Equal(
            [(true, b), (true, c), (false, d), (false, e)],
            listed.Select(s => (s.IsOutgoing, s.Other.FilePath)));
        Assert.Equal(tc.AdjacencyOf(a, b), listed[0].Adjacency);
        Assert.Equal(tc.AdjacencyOf(d, a), listed[2].Adjacency);
    }

    [Fact]
    public void EachAdjacency_CarriesTheGlobal_TheOpenPlaylistsOverride_AndTheEffectiveTransition()
    {
        var (tc, p) = FiveInPAndQ();
        using var _ = tc;
        var (a, b, c) = (p[0], p[1], p[2]);
        tc.Engine.Author(tc.AdjacencyOf(a, b), Crossfade(2));
        tc.Engine.AuthorOverride(tc.AdjacencyOf(a, b), FadeOutAt(1.5));
        tc.Engine.Author(tc.AdjacencyOf(a, c), Crossfade(2));

        var listed = tc.Engine.AdjacenciesInStore(TrackAt(tc, a));

        Assert.Equal(tc.Engine.Transition(tc.AdjacencyOf(a, b)), listed[0].Layers);
        Assert.Equal(Crossfade(2), listed[0].Layers.Global);
        Assert.Equal(FadeOutAt(1.5), listed[0].Layers.Override);
        Assert.Equal(Crossfade(2) with { FadeOutPoint = FadeOutAt(1.5).FadeOutPoint }, listed[0].Layers.Effective);
        Assert.Null(listed[1].Layers.Override);
        Assert.Equal(Crossfade(2), listed[1].Layers.Effective);
        Assert.False(listed[0].PlaylistOnly);
    }

    [Fact]
    public void AnOverrideWithNoGlobal_IsListedAsPlaylistOnly_InItsPlaylistAlone()
    {
        var (tc, p) = FiveInPAndQ();
        using var _ = tc;
        var (a, b, c) = (p[0], p[1], p[2]);
        tc.Engine.Author(tc.AdjacencyOf(a, b), Crossfade(2));
        tc.Engine.AuthorOverride(tc.AdjacencyOf(a, b), FadeOutAt(1.5));
        tc.Engine.AuthorOverride(tc.AdjacencyOf(a, c), Crossfade(1));

        var inP = tc.Engine.AdjacenciesInStore(TrackAt(tc, a));
        Assert.Equal([false, true], inP.Select(s => s.PlaylistOnly));
        Assert.Null(inP[1].Layers.Global);
        Assert.Equal(Crossfade(1), inP[1].Layers.Effective);

        tc.Engine.OpenPlaylist("Q").Wait();
        var inQ = tc.Engine.AdjacenciesInStore(TrackAt(tc, a));

        var only = Assert.Single(inQ);
        Assert.Equal(b, only.Other.FilePath);
        Assert.Null(only.Layers.Override); // P's override is P's
        Assert.Equal(Crossfade(2), only.Layers.Effective);
    }

    [Fact]
    public void ATrackIntoItself_IsListedOnce_AsOutgoing()
    {
        var (tc, p) = FiveInPAndQ();
        using var _ = tc;
        var a = TrackAt(tc, p[0]);
        tc.Engine.Author(new Adjacency(a, a), Crossfade(2));

        var only = Assert.Single(tc.Engine.AdjacenciesInStore(a));

        Assert.True(only.IsOutgoing);
        Assert.Equal(a, only.Other);
    }

    [Fact]
    public void WithNoLibrary_NothingIsListed()
    {
        var sink = new RecordingSink();
        using var engine = new PlaybackEngine(new FakeAudioOutput(), (ITransitionStore?)null, sink);
        using var tc = TestEngine.Create();

        Assert.Empty(engine.TracksInStore());
        Assert.Empty(engine.AdjacenciesInStore(tc.TrackOfLength(@"C:\nowhere\a.wav", 3)));
    }

    // ── TracksInStore ────────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryTrackTheStoreNames_IsListedByTitle_AndAQueuedOneIsTheQueuesOwn()
    {
        var (tc, p) = FiveInPAndQ();
        using var _ = tc;
        var (a, b, c, d, e) = (p[0], p[1], p[2], p[3], p[4]);
        tc.Engine.Author(tc.AdjacencyOf(e, a), Crossfade(2));
        tc.Engine.Author(tc.AdjacencyOf(c, b), Crossfade(2));
        tc.Engine.AuthorOverride(tc.AdjacencyOf(d, a), Crossfade(2)); // an override names its tracks too

        var tracks = tc.Engine.TracksInStore();

        Assert.Equal([a, b, c, d, e], tracks.Select(t => t.FilePath));
        Assert.Equal(TrackAt(tc, c), tracks[2]);
    }

    [Fact]
    public void ATrackNotQueued_IsProbedForItsTitleAndLength_AndOneWhoseFileIsGone_IsListedByItsPath()
    {
        var (tc, p) = FiveInPAndQ();
        using var _ = tc;
        var a = TrackAt(tc, p[0]);
        var far = TestAudio.WriteDcWav("mgr-far.wav", 4, 0.2);
        var gone = Path.Combine(tc.TempDir, "mgr-gone.wav");
        tc.Engine.Author(new Adjacency(a, tc.TrackOf(far)), Crossfade(2));
        tc.Engine.Author(new Adjacency(tc.TrackOfLength(gone, 3), a), Crossfade(2));

        var tracks = tc.Engine.TracksInStore();
        var probed = tracks.Single(t => t.FilePath == far);
        var missing = tracks.Single(t => t.FilePath == gone);

        Assert.Equal(Path.GetFileNameWithoutExtension(far), probed.Title);
        Assert.Equal(4.0, probed.Length.TotalSeconds, 3);
        Assert.Equal("", missing.Title);
        Assert.Equal(gone, missing.ToString());
        var listed = tc.Engine.AdjacenciesInStore(a);
        Assert.Equal([far, gone], listed.Select(s => s.Other.FilePath));
    }

    // ── PreviewStored ────────────────────────────────────────────────────────────────────

    /// <summary>A and B, 20 s each, playing 3 s into A: A → B authored to fade out at 12 s, and
    /// P's override moving it to 9 s.</summary>
    private static (TestEngine Tc, Adjacency AB) OverriddenAndPlaying()
    {
        var (tc, p) = FiveInPAndQ(seconds: 20);
        var ab = tc.AdjacencyOf(p[0], p[1]);
        tc.Engine.Author(ab, Crossfade(12));
        tc.Engine.AuthorOverride(ab, FadeOutAt(9));
        tc.Engine.PlayRow(tc.RowOf(p[0]));
        tc.Output.Advance(3 * Second);
        return (tc, ab);
    }

    [Fact]
    public void PreviewStored_InThePlaylistLayer_PlaysTheOverridesFadeOutPoint_AndHoldsTheQueue()
    {
        var (tc, ab) = OverriddenAndPlaying();
        using var _ = tc;

        tc.Engine.PreviewStored(ab, TransitionLayer.Playlist);
        tc.Output.Advance(Second);

        Assert.Equal(ab, tc.View.Preview!.Adjacency);
        Assert.Equal(5.0, tc.View.Preview.Outgoing!.Value.TotalSeconds, 3); // from 9 − 5 s
        Assert.Equal(Transport.Paused, tc.View.Transport);
        Assert.Equal(3.0, tc.Playhead.TotalSeconds, 3);
        tc.Output.Advance(4 * Second);
        Assert.Equal(PreviewPhase.Transition, tc.View.Preview!.Phase);
        Assert.Equal(9.0, tc.View.Preview.Outgoing!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void PreviewStored_InTheGlobalLayer_PlaysTheGlobalsFadeOutPoint_WithoutTheOverride()
    {
        var (tc, ab) = OverriddenAndPlaying();
        using var _ = tc;

        tc.Engine.PreviewStored(ab, TransitionLayer.Global);
        tc.Output.Advance(Second);

        Assert.Equal(8.0, tc.View.Preview!.Outgoing!.Value.TotalSeconds, 3); // from 12 − 5 s
        tc.Output.Advance(4 * Second);
        Assert.Equal(PreviewPhase.Transition, tc.View.Preview!.Phase);
        Assert.Equal(FadeOutAt(9), tc.Engine.Transition(ab).Override); // nothing reached the store
    }

    [Fact]
    public void PreviewStored_OfAPlaylistOnlyTransition_InTheGlobalLayer_PlaysTheUnauthoredHardCut()
    {
        var (tc, p) = FiveInPAndQ(seconds: 20);
        using var _ = tc;
        var ab = tc.AdjacencyOf(p[0], p[1]);
        tc.Engine.AuthorOverride(ab, Crossfade(9));

        tc.Engine.PreviewStored(ab, TransitionLayer.Global);
        tc.Output.Advance(Second);
        Assert.Equal(16.0, tc.View.Preview!.Outgoing!.Value.TotalSeconds, 3); // 5 s before A's end

        tc.Engine.PreviewStored(ab, TransitionLayer.Playlist);
        tc.Output.Advance(Second);
        Assert.Equal(5.0, tc.View.Preview!.Outgoing!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void PreviewStored_WithATrackWhoseFileIsGone_SaysSo_AndHoldsNothing()
    {
        var (tc, p) = FiveInPAndQ();
        using var _ = tc;
        var gone = new Adjacency(TrackAt(tc, p[0]), tc.TrackOfLength(Path.Combine(tc.TempDir, "mgr-gone.wav"), 3));
        tc.Engine.Author(gone, Crossfade(2));
        tc.Engine.PlayRow(tc.RowOf(p[0]));

        tc.Engine.PreviewStored(gone, TransitionLayer.Playlist);

        Assert.Null(tc.View.Preview);
        Assert.Equal(Transport.Playing, tc.View.Transport);
        Assert.Contains(tc.Notices, n => n.StartsWith("Could not preview \"mgr-gone.wav\""));
    }

    // ── StoreVersion ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheStoreVersion_MovesOnWithEveryWriteAndPlaylistOpened_AndOnlyThen()
    {
        var (tc, p) = FiveInPAndQ();
        using var _ = tc;
        var ab = tc.AdjacencyOf(p[0], p[1]);
        var versions = new List<long> { tc.View.StoreVersion };
        void Moved(Action change)
        {
            change();
            Assert.True(tc.View.StoreVersion > versions[^1], "the store version did not move on");
            versions.Add(tc.View.StoreVersion);
        }

        Moved(() => tc.Engine.Author(ab, Crossfade(2)));
        Moved(() => tc.Engine.AuthorOverride(ab, FadeOutAt(1.5)));
        Moved(() => tc.Engine.UnauthorOverride(ab));
        Moved(() => tc.Engine.Unauthor(ab));
        Moved(() => tc.Engine.OpenPlaylist("Q").Wait());

        tc.Engine.PlayRow(tc.RowOf(p[0]));
        tc.Output.Advance(Second);
        tc.Engine.Pause();
        Assert.Equal(versions[^1], tc.View.StoreVersion);
    }
}
