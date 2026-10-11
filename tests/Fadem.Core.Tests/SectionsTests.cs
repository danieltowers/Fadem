using System.Text;
using Fadem.Core.Domain;
using Fadem.Core.Playback;
using Fadem.Core.Transitions;

namespace Fadem.Core.Tests;

/// <summary>Phase 6 issue 09: sections organise a playlist. A section is a named run of
/// consecutive rows in the playlist's own order; it only labels them, so nothing about what plays
/// changes. Seen through the view: each row's <see cref="QueueRow.Section"/>, and
/// <see cref="QueueRow.StartsSection"/> on the row a header is drawn above.</summary>
public sealed class SectionsTests
{
    private static string Tone(string name) => TestAudio.WriteToneWav($"sec-{name}.wav", 2, 1000);

    private static string[] Tones(int count) => Enumerable.Range(0, count).Select(i => Tone($"t{i}")).ToArray();

    private static string PlaylistPath(TestEngine tc, string name) =>
        Path.Combine(tc.Library.PlaylistsFolder, name + ".m3u8");

    /// <summary>The playlist's file, as its comment lines that are not <c>#EXTINF</c>, and its
    /// entries by index into <paramref name="p"/>.</summary>
    private static string FileLayout(TestEngine tc, string name, string[] p) => string.Join(" ",
        File.ReadAllLines(PlaylistPath(tc, name))
            .Where(l => !l.StartsWith("#EXTINF") && l != "#EXTM3U")
            .Select(l => l.StartsWith('#') ? l : Array.IndexOf(p, l).ToString()));

    /// <summary>Opens a new playlist and then <paramref name="name"/> again, so its rows are read
    /// back from its file.</summary>
    private static void Reopen(TestEngine tc, string name)
    {
        tc.Engine.OpenPlaylist("Other").Wait();
        tc.Engine.OpenPlaylist(name).Wait();
    }

    /// <summary>Each row's section name, or "-" for none, with a "|" before a row that starts one.</summary>
    private static string Layout(TestEngine tc) => string.Join(" ", tc.View.Queue.Select(r =>
        (r.StartsSection ? "|" : "") + (r.Section?.Name ?? "-")));

    [Fact]
    public void StartingASection_AtARow_TakesThatRowAndEveryRowAfterIt()
    {
        using var tc = TestEngine.Create();
        var p = Tones(4);
        tc.Add(p);

        tc.Engine.StartSection(tc.RowOf(p[1]), "Intro");

        Assert.Equal("- |Intro Intro Intro", Layout(tc));
    }

    [Fact]
    public void StartingASection_InsideAnother_SplitsIt_AndTakesOnlyItsOwnRows()
    {
        using var tc = TestEngine.Create();
        var p = Tones(5);
        tc.Add(p);
        tc.Engine.StartSection(tc.RowOf(p[0]), "Intro");
        tc.Engine.StartSection(tc.RowOf(p[3]), "End");

        tc.Engine.StartSection(tc.RowOf(p[1]), "Middle");

        Assert.Equal("|Intro |Middle Middle |End End", Layout(tc));
    }

    [Fact]
    public void StartingASection_WhereOneStarts_OrWithABlankName_IsRefusedWithANotice()
    {
        using var tc = TestEngine.Create();
        var p = Tones(3);
        tc.Add(p);
        tc.Engine.StartSection(tc.RowOf(p[1]), "Intro");
        var notices = tc.Notices.Count;

        tc.Engine.StartSection(tc.RowOf(p[1]), "Again");
        tc.Engine.StartSection(tc.RowOf(p[2]), "  ");

        Assert.Equal("- |Intro Intro", Layout(tc));
        Assert.Equal(notices + 2, tc.Notices.Count);
    }

    [Fact]
    public void AStaleRow_IsANotice()
    {
        using var tc = TestEngine.Create();
        var p = Tones(2);
        tc.Add(p);
        var gone = tc.RowOf(p[0]);
        tc.Engine.Remove([gone]);
        var notices = tc.Notices.Count;

        tc.Engine.StartSection(gone, "Intro");

        Assert.Equal("-", Layout(tc));
        Assert.Equal(notices + 1, tc.Notices.Count);
    }

    [Fact]
    public void Sections_AreWrittenToThePlaylistsFile_AsExtgrpLines()
    {
        using var tc = TestEngine.Create();
        var p = Tones(4);
        tc.Add(p);

        tc.Engine.StartSection(tc.RowOf(p[1]), "Intro");
        tc.Engine.StartSection(tc.RowOf(p[3]), "End");

        Assert.Equal(
            "0 #FADEM-SECTION:colour=0 #EXTGRP:Intro 1 2 #FADEM-SECTION:colour=1 #EXTGRP:End 3",
            FileLayout(tc, "Untitled", p));
    }

    [Fact]
    public void Sections_ComeBack_WhenThePlaylistIsOpenedAgain()
    {
        using var tc = TestEngine.Create();
        var p = Tones(4);
        tc.Add(p);
        tc.Engine.RenamePlaylist("Untitled", "Set");
        File.WriteAllText(PlaylistPath(tc, "Other"), "#EXTM3U\n");
        tc.Engine.StartSection(tc.RowOf(p[1]), "Intro");
        tc.Engine.StartSection(tc.RowOf(p[3]), "End");
        tc.Engine.CollapseSection(SectionAt(tc, p[3]), true);
        var before = tc.View.Queue.Select(r => (r.Section?.Colour, r.Section?.Collapsed)).ToArray();

        Reopen(tc, "Set");

        Assert.Equal("- |Intro Intro |End", Layout(tc));
        Assert.Equal(before, tc.View.Queue.Select(r => (r.Section?.Colour, r.Section?.Collapsed)).ToArray());
    }

    private static SectionId SectionAt(TestEngine tc, string path) =>
        tc.View.Queue.Single(r => r.Track.FilePath == path).Section!.Id;

    [Fact]
    public void RenamingASection_RenamesItInTheViewAndTheFile()
    {
        using var tc = TestEngine.Create();
        var p = Tones(2);
        tc.Add(p);
        tc.Engine.StartSection(tc.RowOf(p[1]), "Intro");

        tc.Engine.RenameSection(SectionAt(tc, p[1]), " Opening ");

        Assert.Equal("- |Opening", Layout(tc));
        Assert.Equal("0 #FADEM-SECTION:colour=0 #EXTGRP:Opening 1", FileLayout(tc, "Untitled", p));
    }

    [Fact]
    public void RecolouringAndCollapsing_AreShownAndKept_AndAColourOffThePalette_IsRefused()
    {
        using var tc = TestEngine.Create();
        var p = Tones(2);
        tc.Add(p);
        tc.Engine.StartSection(tc.RowOf(p[0]), "Intro");
        var intro = SectionAt(tc, p[0]);
        var notices = tc.Notices.Count;

        tc.Engine.RecolourSection(intro, 5);
        tc.Engine.RecolourSection(intro, SectionView.Colours);
        tc.Engine.CollapseSection(intro, true);

        Assert.Equal(new SectionView(intro, "Intro", 5, Collapsed: true), tc.View.Queue[1].Section);
        Assert.Equal(notices + 1, tc.Notices.Count);
        Assert.Equal("#FADEM-SECTION:colour=5;collapsed=1 #EXTGRP:Intro 0 1", FileLayout(tc, "Untitled", p));
    }

    [Fact]
    public void RemovingASection_PutsItsRowsInTheSectionAbove_OrInNoneAtTheTop()
    {
        using var tc = TestEngine.Create();
        var p = Tones(4);
        tc.Add(p);
        tc.Engine.StartSection(tc.RowOf(p[0]), "Intro");
        tc.Engine.StartSection(tc.RowOf(p[1]), "Middle");
        tc.Engine.StartSection(tc.RowOf(p[3]), "End");

        tc.Engine.RemoveSection(SectionAt(tc, p[1]));
        Assert.Equal("|Intro Intro Intro |End", Layout(tc));

        tc.Engine.RemoveSection(SectionAt(tc, p[0]));
        Assert.Equal("- - - |End", Layout(tc));
        Assert.Equal("0 1 2 #FADEM-SECTION:colour=2 #EXTGRP:End 3", FileLayout(tc, "Untitled", p));
    }

    private static string Order(TestEngine tc, string[] p) =>
        string.Join(" ", tc.View.Queue.Select(r => Array.IndexOf(p, r.Track.FilePath)));

    [Fact]
    public void MovingASection_MovesAllItsRows_PastTheNextSection_LeavingRowsAboveTheFirstAlone()
    {
        using var tc = TestEngine.Create();
        var p = Tones(5);
        tc.Add(p);
        tc.Engine.StartSection(tc.RowOf(p[1]), "Intro");
        tc.Engine.StartSection(tc.RowOf(p[2]), "Middle");
        tc.Engine.StartSection(tc.RowOf(p[4]), "End");

        tc.Engine.MoveSection(SectionAt(tc, p[2]), +1);
        Assert.Equal("0 1 4 2 3", Order(tc, p));
        Assert.Equal("- |Intro |End |Middle Middle", Layout(tc));

        tc.Engine.MoveSection(SectionAt(tc, p[2]), -2);
        Assert.Equal("0 2 3 1 4", Order(tc, p));

        tc.Engine.MoveSection(SectionAt(tc, p[2]), -1); // already first: nothing moves
        Assert.Equal("0 2 3 1 4", Order(tc, p));
        Assert.Equal("0 #FADEM-SECTION:colour=1 #EXTGRP:Middle 2 3 #FADEM-SECTION:colour=0 #EXTGRP:Intro 1 "
            + "#FADEM-SECTION:colour=2 #EXTGRP:End 4", FileLayout(tc, "Untitled", p));
    }

    [Fact]
    public void RowsMovedBeforeARow_JoinItsSection_AndAtTheEnd_JoinTheLastSection()
    {
        using var tc = TestEngine.Create();
        var p = Tones(4);
        tc.Add(p);
        tc.Engine.StartSection(tc.RowOf(p[0]), "Intro");
        tc.Engine.StartSection(tc.RowOf(p[2]), "End");

        tc.Engine.Move([tc.RowOf(p[3])], before: tc.RowOf(p[1]));
        Assert.Equal("0 3 1 2", Order(tc, p));
        Assert.Equal("|Intro Intro Intro |End", Layout(tc));

        tc.Engine.Move([tc.RowOf(p[0])], before: null);
        Assert.Equal("3 1 2 0", Order(tc, p));
        Assert.Equal("|Intro Intro |End End", Layout(tc));
    }

    [Fact]
    public void RowsMovedAfterARow_JoinItsSection_SoTheyCanEndTheSectionAboveAHeader()
    {
        using var tc = TestEngine.Create();
        var p = Tones(4);
        tc.Add(p);
        tc.Engine.StartSection(tc.RowOf(p[0]), "Intro");
        tc.Engine.StartSection(tc.RowOf(p[2]), "End");

        tc.Engine.MoveAfter([tc.RowOf(p[3])], after: tc.RowOf(p[1]));

        Assert.Equal("0 1 3 2", Order(tc, p));
        Assert.Equal("|Intro Intro Intro |End", Layout(tc));
    }

    [Fact]
    public void AddedFiles_JoinTheSectionOfTheRowTheyLandBefore_OrAtTheEnd_TheLastSection()
    {
        using var tc = TestEngine.Create();
        var p = Tones(5);
        tc.Add(p[0], p[1]);
        tc.Engine.StartSection(tc.RowOf(p[0]), "Intro");
        tc.Engine.StartSection(tc.RowOf(p[1]), "End");

        tc.Add(p[2]);
        tc.Engine.Add([p[3]], before: tc.RowOf(p[1])).Wait();
        tc.Engine.Add([p[4]], before: tc.RowOf(p[0])).Wait();

        Assert.Equal("4 0 3 1 2", Order(tc, p));
        Assert.Equal("|Intro Intro |End End End", Layout(tc));
    }

    [Fact]
    public void ASectionWhoseRowsAreAllRemoved_IsGone()
    {
        using var tc = TestEngine.Create();
        var p = Tones(2);
        tc.Add(p);
        tc.Engine.StartSection(tc.RowOf(p[1]), "End");
        var end = SectionAt(tc, p[1]);

        tc.Engine.Remove([tc.RowOf(p[1])]);
        tc.Add(p[1]);
        var notices = tc.Notices.Count;
        tc.Engine.RenameSection(end, "Again");

        Assert.Equal("- -", Layout(tc));
        Assert.Equal(notices + 1, tc.Notices.Count);
        Assert.Equal("0 1", FileLayout(tc, "Untitled", p));
    }

    [Fact]
    public void OpeningAnotherPlaylist_ShowsOnlyItsOwnSections()
    {
        using var tc = TestEngine.Create();
        var p = Tones(3);
        tc.Add(p[0], p[1]);
        tc.Engine.StartSection(tc.RowOf(p[0]), "Intro");
        File.WriteAllLines(PlaylistPath(tc, "Plain"), ["#EXTM3U", p[2]], new UTF8Encoding(false));

        tc.Engine.OpenPlaylist("Plain").Wait();
        tc.Add(p[0]);

        Assert.Equal("- -", Layout(tc));
    }

    /// <summary>A playlist as another player writes one: the group above every entry.</summary>
    private static string[] ForeignLines(string[] p) =>
        ["#EXTM3U", "#EXTGRP:Intro", p[0], "#EXTGRP:Intro", p[1], "#EXTGRP:End", "#EXTINF:2,End", p[2]];

    [Fact]
    public void AnotherPlayersGroups_OpenAsSections_OnePerRunOfTheSameName()
    {
        using var tc = TestEngine.Create();
        var p = Tones(3);
        File.WriteAllLines(PlaylistPath(tc, "Foreign"), ForeignLines(p), new UTF8Encoding(false));

        tc.Engine.OpenPlaylist("Foreign").Wait();

        Assert.Equal("|Intro Intro |End", Layout(tc));
        Assert.Equal(new int?[] { 0, 0, 1 }, tc.View.Queue.Select(r => r.Section?.Colour).ToArray());
    }

    [Fact]
    public void ImportingAPlaylist_KeepsItsGroupsAsSections()
    {
        using var tc = TestEngine.Create();
        var p = Tones(3);
        var source = Path.Combine(tc.TempDir, "Gig.m3u8");
        File.WriteAllLines(source, ForeignLines(p), new UTF8Encoding(false));

        tc.Engine.ImportPlaylists([source]).Wait();

        Assert.Equal("Gig", tc.View.OpenPlaylist);
        Assert.Equal("|Intro Intro |End", Layout(tc));
    }

    [Fact]
    public void ASectionWhoseFilesAreAllGone_DoesNotOpen()
    {
        using var tc = TestEngine.Create();
        var p = Tones(2);
        var gone = Path.Combine(tc.TempDir, "gone.wav");
        File.WriteAllLines(PlaylistPath(tc, "Holes"),
            ["#EXTM3U", p[0], "#EXTGRP:Lost", gone, "#FADEM-SECTION:colour=99", "#EXTGRP:Kept", p[1]],
            new UTF8Encoding(false));

        tc.Engine.OpenPlaylist("Holes").Wait();

        Assert.Equal("- |Kept", Layout(tc));
        Assert.Equal(1, tc.View.Queue[1].Section!.Colour); // off the palette: the next after Lost's 0
    }

    [Fact]
    public void UnderShuffle_NoHeaderIsDrawn_AndRowsMovedKeepTheirSections()
    {
        using var tc = TestEngine.Create();
        var p = Tones(4);
        tc.Add(p);
        tc.Engine.StartSection(tc.RowOf(p[0]), "Intro");
        tc.Engine.StartSection(tc.RowOf(p[2]), "End");

        tc.Engine.SetShuffle(true);
        tc.Engine.Move([tc.RowOf(p[3])], before: tc.View.Queue[1].Id);

        Assert.DoesNotContain(tc.View.Queue, r => r.StartsSection);
        Assert.Equal("End", tc.View.Queue.Single(r => r.Track.FilePath == p[3]).Section!.Name);
        tc.Engine.SetShuffle(false);
        Assert.Equal("0 1 2 3", Order(tc, p));
        Assert.Equal("|Intro Intro |End End", Layout(tc));
    }

    [Fact]
    public void ATransitionAcrossASectionBoundary_PlaysAsItWouldWithoutOne()
    {
        const int Second = 44100;
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("sec-xa.wav", 3, 1000);
        var b = TestAudio.WriteToneWav("sec-xb.wav", 3, 2000);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
            FadeInPoint = TimeSpan.Zero,
            FadeOutDuration = TimeSpan.FromSeconds(1),
            FadeInDuration = TimeSpan.FromSeconds(1),
        });
        tc.Engine.StartSection(tc.RowOf(a), "Intro");
        tc.Engine.StartSection(tc.RowOf(b), "End");

        tc.Engine.Play();
        tc.Output.Advance(Second + Second / 2); // halfway through A → B

        Assert.Equal(b, tc.View.Incoming!.Row.Track.FilePath);
        Assert.True(tc.Rendered.ToneAmplitude(2000, Second + Second / 2 - 2048) > 0.1, "B must be fading in");
    }

    [Fact]
    public void SavingTheQueueAsAPlaylist_WritesItsSections()
    {
        using var tc = TestEngine.Create();
        var p = Tones(2);
        tc.Add(p);
        tc.Engine.StartSection(tc.RowOf(p[1]), "End");
        var export = Path.Combine(tc.TempDir, "export.m3u8");

        tc.Engine.SaveAsPlaylist(export);

        Assert.Equal(new[] { "#FADEM-SECTION:colour=0", "#EXTGRP:End" },
            File.ReadAllLines(export).Where(l => l.StartsWith("#FADEM") || l.StartsWith("#EXTGRP")));
    }

    [Fact]
    public void WhileAPlaylistLoads_ItsSectionsCannotBeEdited_SoNoneIsSplit()
    {
        using var tc = TestEngine.Create();
        var p = Tones(3);
        File.WriteAllLines(PlaylistPath(tc, "Set"),
            ["#EXTM3U", "#EXTGRP:Intro", p[0], p[1], "#EXTGRP:End", p[2]], new UTF8Encoding(false));
        var real = tc.Engine.ResolvePath;
        var notices = 0;
        tc.Engine.ResolvePath = path =>
        {
            if (path == p[2])
            {
                notices = tc.Notices.Count;
                tc.Engine.MoveSection(SectionAt(tc, p[0]), +1);
                tc.Engine.StartSection(tc.RowOf(p[1]), "Middle");
                notices = tc.Notices.Count - notices;
            }
            return real(path);
        };

        tc.Engine.OpenPlaylist("Set").Wait();

        Assert.Equal(2, notices);
        Assert.Equal("|Intro Intro |End", Layout(tc));
    }

    [Fact]
    public void EachNewSection_TakesTheColourAfterTheSectionAbove()
    {
        using var tc = TestEngine.Create();
        var p = Tones(4);
        tc.Add(p);

        tc.Engine.StartSection(tc.RowOf(p[0]), "A");
        tc.Engine.StartSection(tc.RowOf(p[1]), "B");
        tc.Engine.StartSection(tc.RowOf(p[2]), "C");
        tc.Engine.StartSection(tc.RowOf(p[3]), "D");

        Assert.Equal(new int?[] { 0, 1, 2, 3 }, tc.View.Queue.Select(r => r.Section?.Colour).ToArray());
    }

    [Fact]
    public void TheView_SaysWhichWayEachSectionCanMove_AndUnderShuffleNeither()
    {
        using var tc = TestEngine.Create();
        var p = Tones(3);
        tc.Add(p);
        tc.Engine.StartSection(tc.RowOf(p[0]), "A");
        tc.Engine.StartSection(tc.RowOf(p[1]), "B");
        tc.Engine.StartSection(tc.RowOf(p[2]), "C");

        Assert.Equal(new[] { (false, true), (true, true), (true, false) },
            tc.View.Queue.Select(r => (r.Section!.CanMoveUp, r.Section.CanMoveDown)));
        tc.Engine.SetShuffle(true);
        Assert.All(tc.View.Queue, r => Assert.False(r.Section!.CanMoveUp || r.Section.CanMoveDown));
    }
}
