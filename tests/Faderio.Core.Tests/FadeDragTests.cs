using System.Text;
using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Settings;

namespace Faderio.Core.Tests;

/// <summary>Phase 6 issue 11: the seek bar's fade marks and wedges dragged. A mark moves its end
/// of the fade and keeps the other; a wedge slides the whole fade and keeps its duration. The
/// engine does the arithmetic from playheads, writes the layer the value came from, and saves
/// once, as <c>Author</c> does.</summary>
public sealed class FadeDragTests
{
    private const int Rate = 44100;

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static Playhead At(double seconds) => Playhead.FromMilliseconds((int)Math.Round(seconds * 1000));

    private static double? Seconds(Point? point) => point?.TotalSeconds;

    /// <summary>A → B fading out at 8 s for 2 s, B fading in from 1 s for 3 s.</summary>
    private static TransitionSpec Fade(FadePointSpec? fadeOutPoint = null) => new()
    {
        FadeOutPoint = fadeOutPoint ?? new FadePointSpec(S(8), FromEnd: false),
        FadeOutDuration = S(2),
        FadeInPoint = new FadePointSpec(S(1), FromEnd: false),
        FadeInDuration = S(3),
        Curve = FadeCurveKind.Linear,
    };

    /// <summary>A then B, 12 s each, with A → B authored as <see cref="Fade"/> unless told
    /// otherwise.</summary>
    private static (TestEngine Tc, string A, string B) Pair(string name, TransitionSpec? spec = null, PlayerSettings? settings = null)
    {
        var tc = TestEngine.Create(settings);
        var a = TestAudio.WriteDcWav($"drag-{name}-a.wav", 12, 0.4);
        var b = TestAudio.WriteDcWav($"drag-{name}-b.wav", 12, 0.1);
        tc.Add(a, b);
        if (spec is not null)
        {
            tc.Store.Upsert(a, b, spec);
        }
        return (tc, a, b);
    }

    private static TransitionSpec? Stored(TestEngine tc, string a, string b) => tc.Store.Get(a, b);

    // ── The marks ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheFadeOutStartMark_MovesThePoint_AndKeepsTheEnd()
    {
        var (tc, a, b) = Pair("outstart", Fade());
        using var _ = tc;
        tc.Engine.PlayRow(tc.RowOf(a));

        var refusals = tc.Engine.MoveFadeMark(tc.RowOf(a), FadeMark.FadeOutStart, At(7));

        Assert.Empty(refusals);
        Assert.Equal(Fade() with { FadeOutPoint = new FadePointSpec(S(7), false), FadeOutDuration = S(3) }, Stored(tc, a, b));
        Assert.Equal((7.0, 10.0), (Seconds(tc.View.Attended!.Marks.FadeOutStart), Seconds(tc.View.Attended.Marks.FadeOutEnd)));
    }

    [Fact]
    public void TheFadeOutEndMark_ChangesTheDuration_AndKeepsThePoint()
    {
        var (tc, a, b) = Pair("outend", Fade());
        using var _ = tc;
        tc.Engine.PlayRow(tc.RowOf(a));

        tc.Engine.MoveFadeMark(tc.RowOf(a), FadeMark.FadeOutEnd, At(11));

        Assert.Equal(Fade() with { FadeOutDuration = S(3) }, Stored(tc, a, b));
        Assert.Equal((8.0, 11.0), (Seconds(tc.View.Attended!.Marks.FadeOutStart), Seconds(tc.View.Attended.Marks.FadeOutEnd)));
    }

    [Fact]
    public void TheFadeInStartMark_MovesThePoint_AndKeepsTheEnd()
    {
        var (tc, a, b) = Pair("instart", Fade());
        using var _ = tc;
        tc.Engine.PlayRow(tc.RowOf(b));

        tc.Engine.MoveFadeMark(tc.RowOf(b), FadeMark.FadeInStart, At(2));

        Assert.Equal(Fade() with { FadeInPoint = new FadePointSpec(S(2), false), FadeInDuration = S(2) }, Stored(tc, a, b));
        Assert.Equal((2.0, 4.0), (Seconds(tc.View.Attended!.Marks.FadeInStart), Seconds(tc.View.Attended.Marks.FadeInEnd)));
    }

    [Fact]
    public void TheFadeInEndMark_ChangesTheDuration_AndKeepsThePoint()
    {
        var (tc, a, b) = Pair("inend", Fade());
        using var _ = tc;
        tc.Engine.PlayRow(tc.RowOf(b));

        tc.Engine.MoveFadeMark(tc.RowOf(b), FadeMark.FadeInEnd, At(5));

        Assert.Equal(Fade() with { FadeInDuration = S(4) }, Stored(tc, a, b));
        Assert.Equal((1.0, 5.0), (Seconds(tc.View.Attended!.Marks.FadeInStart), Seconds(tc.View.Attended.Marks.FadeInEnd)));
    }

    // ── The wedges ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheFadeOutWedge_SlidesByTheDrag_AndKeepsItsDuration()
    {
        var (tc, a, b) = Pair("outwedge", Fade());
        using var _ = tc;
        tc.Engine.PlayRow(tc.RowOf(a));

        tc.Engine.MoveFade(tc.RowOf(a), FadeSide.FadeOut, At(9), At(6));

        Assert.Equal(Fade() with { FadeOutPoint = new FadePointSpec(S(5), false) }, Stored(tc, a, b));
        Assert.Equal((5.0, 7.0), (Seconds(tc.View.Attended!.Marks.FadeOutStart), Seconds(tc.View.Attended.Marks.FadeOutEnd)));
    }

    [Fact]
    public void TheFadeInWedge_SlidesByTheDrag_AndKeepsItsDuration()
    {
        var (tc, a, b) = Pair("inwedge", Fade());
        using var _ = tc;
        tc.Engine.PlayRow(tc.RowOf(b));

        tc.Engine.MoveFade(tc.RowOf(b), FadeSide.FadeIn, At(2), At(3.5));

        Assert.Equal(Fade() with { FadeInPoint = new FadePointSpec(S(2.5), false) }, Stored(tc, a, b));
        Assert.Equal((2.5, 5.5), (Seconds(tc.View.Attended!.Marks.FadeInStart), Seconds(tc.View.Attended.Marks.FadeInEnd)));
    }

    [Fact]
    public void AWedgeDraggedPastTheStart_StopsAtTheStart()
    {
        var (tc, a, b) = Pair("wedgeclamp", Fade());
        using var _ = tc;
        tc.Engine.PlayRow(tc.RowOf(b));

        tc.Engine.MoveFade(tc.RowOf(b), FadeSide.FadeIn, At(2), At(0));

        Assert.Equal(Fade() with { FadeInPoint = new FadePointSpec(S(0), false) }, Stored(tc, a, b));
    }

    [Fact]
    public void AWedgeDraggedPastTheEnd_StopsWithItsWholeDurationInTheTrack()
    {
        var (tc, a, b) = Pair("wedgeend", Fade());
        using var _ = tc;
        tc.Engine.PlayRow(tc.RowOf(a));

        tc.Engine.MoveFade(tc.RowOf(a), FadeSide.FadeOut, At(9), At(12));

        Assert.Equal(Fade() with { FadeOutPoint = new FadePointSpec(S(10), false) }, Stored(tc, a, b));
        Assert.Equal((10.0, 12.0), (Seconds(tc.View.Attended!.Marks.FadeOutStart), Seconds(tc.View.Attended.Marks.FadeOutEnd)));
    }

    // ── From the end ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void AFromEndFadeOutPoint_StaysFromTheEnd_AtTheMovedPosition()
    {
        var fromEnd = Fade(new FadePointSpec(S(4), FromEnd: true)); // 8 s from the start
        var (tc, a, b) = Pair("fromend", fromEnd);
        using var _ = tc;
        tc.Engine.PlayRow(tc.RowOf(a));

        tc.Engine.MoveFadeMark(tc.RowOf(a), FadeMark.FadeOutStart, At(7));
        Assert.Equal(fromEnd with { FadeOutPoint = new FadePointSpec(S(5), true), FadeOutDuration = S(3) }, Stored(tc, a, b));

        tc.Engine.MoveFade(tc.RowOf(a), FadeSide.FadeOut, At(8), At(6));
        Assert.Equal(fromEnd with { FadeOutPoint = new FadePointSpec(S(7), true), FadeOutDuration = S(3) }, Stored(tc, a, b));
        Assert.Equal(5.0, Seconds(tc.View.Attended!.Marks.FadeOutStart));
    }

    // ── Which layer ──────────────────────────────────────────────────────────────────────

    private static void WritePlaylist(TestEngine tc, string name, params string[] paths) =>
        File.WriteAllLines(Path.Combine(tc.Library.PlaylistsFolder, name + ".m3u8"), ["#EXTM3U", .. paths], new UTF8Encoding(false));

    /// <summary>Playlist P holding A then B, open, with A → B authored globally as
    /// <see cref="Fade"/>.</summary>
    private static (TestEngine Tc, string A, string B) InPlaylist(string name)
    {
        var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav($"drag-{name}-a.wav", 12, 0.4);
        var b = TestAudio.WriteDcWav($"drag-{name}-b.wav", 12, 0.1);
        WritePlaylist(tc, "P", a, b);
        tc.Engine.OpenPlaylist("P").Wait();
        tc.Store.Upsert(a, b, Fade());
        return (tc, a, b);
    }

    [Fact]
    public void WithAnOverrideSettingTheDraggedParameter_OnlyTheOverrideChanges()
    {
        var (tc, a, b) = InPlaylist("override");
        using var _ = tc;
        var overridden = new TransitionSpec { FadeOutPoint = new FadePointSpec(S(6), false) };
        tc.Engine.AuthorOverride(tc.AdjacencyOf(a, b), overridden);
        tc.Engine.PlayRow(tc.RowOf(a));

        tc.Engine.MoveFade(tc.RowOf(a), FadeSide.FadeOut, At(7), At(5));

        Assert.Equal(new TransitionSpec { FadeOutPoint = new FadePointSpec(S(4), false) }, tc.Store.GetOverride("P", a, b));
        Assert.Equal(Fade(), Stored(tc, a, b));
        Assert.Equal((4.0, 6.0), (Seconds(tc.View.Attended!.Marks.FadeOutStart), Seconds(tc.View.Attended.Marks.FadeOutEnd)));
    }

    [Fact]
    public void WithoutAnOverrideSettingTheDraggedParameter_OnlyTheGlobalChanges()
    {
        var (tc, a, b) = InPlaylist("global");
        using var _ = tc;
        var overridden = new TransitionSpec { FadeOutPoint = new FadePointSpec(S(6), false) };
        tc.Engine.AuthorOverride(tc.AdjacencyOf(a, b), overridden);
        tc.Engine.PlayRow(tc.RowOf(a));

        tc.Engine.MoveFadeMark(tc.RowOf(a), FadeMark.FadeOutEnd, At(9)); // the duration: global

        Assert.Equal(overridden, tc.Store.GetOverride("P", a, b));
        Assert.Equal(Fade() with { FadeOutDuration = S(3) }, Stored(tc, a, b));
        Assert.Equal((6.0, 9.0), (Seconds(tc.View.Attended!.Marks.FadeOutStart), Seconds(tc.View.Attended.Marks.FadeOutEnd)));
    }

    [Fact]
    public void WithNoOverride_AndAPlaylistOpen_TheGlobalChanges()
    {
        var (tc, a, b) = InPlaylist("noverride");
        using var _ = tc;
        tc.Engine.PlayRow(tc.RowOf(a));

        tc.Engine.MoveFadeMark(tc.RowOf(a), FadeMark.FadeOutStart, At(7));

        Assert.Null(tc.Store.GetOverride("P", a, b));
        Assert.Equal(Fade() with { FadeOutPoint = new FadePointSpec(S(7), false), FadeOutDuration = S(3) }, Stored(tc, a, b));
    }

    // ── Unauthored ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void AnUnauthoredAdjacencyUnderTheDefaultCrossfade_BecomesAuthoredWithExplicitValues()
    {
        var settings = new PlayerSettings { UnauthoredAdjacency = UnauthoredAdjacency.DefaultCrossfade, DefaultCrossfadeSeconds = 2 };
        var (tc, a, b) = Pair("default", settings: settings);
        using var _ = tc;
        tc.Engine.PlayRow(tc.RowOf(a));
        Assert.Equal((10.0, 12.0), (Seconds(tc.View.Attended!.Marks.FadeOutStart), Seconds(tc.View.Attended.Marks.FadeOutEnd)));

        tc.Engine.MoveFadeMark(tc.RowOf(a), FadeMark.FadeOutStart, At(9));

        Assert.Equal(new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(S(9), false),
            FadeOutDuration = S(3),
            FadeInPoint = new FadePointSpec(S(0), false),
            FadeInDuration = S(2),
        }, Stored(tc, a, b));
        Assert.Equal((9.0, 12.0), (Seconds(tc.View.Attended!.Marks.FadeOutStart), Seconds(tc.View.Attended.Marks.FadeOutEnd)));
    }

    [Fact]
    public void BlankDurations_BecomeExplicit()
    {
        var spec = new TransitionSpec { FadeOutPoint = new FadePointSpec(S(6), false) };
        var (tc, a, b) = Pair("blank", spec, new PlayerSettings { BlankDurationSeconds = 2 });
        using var _ = tc;
        tc.Engine.PlayRow(tc.RowOf(a));

        tc.Engine.MoveFadeMark(tc.RowOf(a), FadeMark.FadeOutEnd, At(9));

        Assert.Equal(new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(S(6), false),
            FadeOutDuration = S(3),
            FadeInPoint = new FadePointSpec(S(0), false),
            FadeInDuration = S(2),
        }, Stored(tc, a, b));
    }

    // ── Refused, undone, stale ───────────────────────────────────────────────────────────

    [Fact]
    public void ADropPastTheOtherEdge_IsRefused_AndNothingIsSaved()
    {
        var (tc, a, b) = Pair("past", Fade());
        using var _ = tc;
        tc.Engine.PlayRow(tc.RowOf(a));

        var refusals = tc.Engine.MoveFadeMark(tc.RowOf(a), FadeMark.FadeOutEnd, At(7));

        Assert.Equal(TransitionField.FadeOutDuration, Assert.Single(refusals).Field);
        Assert.Equal(Fade(), Stored(tc, a, b));
        Assert.Null(tc.View.NextUndo);
        Assert.Contains(tc.Notices, n => n.StartsWith("Not saved") && n.Contains("cannot be negative"));
    }

    [Fact]
    public void OneDrag_IsOneUndoStep()
    {
        var (tc, a, b) = Pair("undo", Fade());
        using var _ = tc;
        tc.Engine.PlayRow(tc.RowOf(a));

        tc.Engine.MoveFadeMark(tc.RowOf(a), FadeMark.FadeOutStart, At(7));
        Assert.Equal(1, tc.Notices.Count(n => n.StartsWith("Saved")));

        Assert.Equal(tc.AdjacencyOf(a, b), tc.Engine.Undo());
        Assert.Equal(Fade(), Stored(tc, a, b));
        Assert.Null(tc.View.NextUndo);
    }

    [Fact]
    public void AStaleRow_OrAFadeTheRowDoesNotHave_DoesNothing_AndSaysSo()
    {
        var (tc, a, b) = Pair("stale", Fade());
        using var _ = tc;
        var rowA = tc.RowOf(a);

        tc.Engine.MoveFadeMark(rowA, FadeMark.FadeInStart, At(1)); // the first row: no fade-in
        Assert.Contains(tc.Notices, n => n.StartsWith("Nothing moved") && n.Contains("fade-in"));

        tc.Engine.MoveFade(tc.RowOf(b), FadeSide.FadeOut, At(1), At(2)); // the last row: no fade-out
        Assert.Contains(tc.Notices, n => n.StartsWith("Nothing moved") && n.Contains("fade-out"));

        tc.Engine.Remove([rowA]);
        tc.Engine.MoveFadeMark(rowA, FadeMark.FadeOutStart, At(7));
        Assert.Contains(tc.Notices, n => n.StartsWith("Nothing moved") && n.Contains("no longer"));

        Assert.Equal(Fade(), Stored(tc, a, b));
        Assert.Null(tc.View.NextUndo);
    }

    [Fact]
    public void AHardCut_HasNoFadeToMove()
    {
        var (tc, a, b) = Pair("hardcut");
        using var _ = tc;

        tc.Engine.MoveFadeMark(tc.RowOf(a), FadeMark.FadeOutEnd, At(11));

        Assert.Contains(tc.Notices, n => n.StartsWith("Nothing moved") && n.Contains("hard cut"));
        Assert.Null(Stored(tc, a, b));
    }

    // ── Through the chain ────────────────────────────────────────────────────────────────

    [Fact]
    public void ADragOnTheComingBoundary_WhilePlaying_Reschedules()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("drag-live-a.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("drag-live-b.wav", 3, 0.1);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(S(2), false),
            FadeOutDuration = S(0.5),
            FadeInPoint = TimeSpan.Zero,
            FadeInDuration = S(0.5),
            Curve = FadeCurveKind.Linear,
        });
        tc.Engine.Play();
        tc.Output.Advance(Rate / 2);

        tc.Engine.MoveFadeMark(tc.RowOf(a), FadeMark.FadeOutStart, At(1));
        tc.Output.Advance(3 * Rate);

        var left = tc.Output.Blocks.SelectMany(block => block).Where((_, i) => i % 2 == 0).ToArray();
        var frame = Array.FindIndex(left, s => Math.Abs(s - left[0]) > 1e-5);
        Assert.InRange((double)frame / Rate, 0.99, 1.01);
    }
}
