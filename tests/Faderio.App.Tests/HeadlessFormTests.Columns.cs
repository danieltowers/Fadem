using System.Reflection;
using Faderio.Shell;
using Faderio.App;
using Faderio.Core.Persistence;
using Faderio.Core.Tests;

namespace Faderio.App.Tests;

/// <summary>The queue's configurable columns (playlists issue 06, D25), on the headless main window.</summary>
public sealed partial class HeadlessFormTests
{
    private static string[] Headings(MainForm form) =>
        form._queueList.Columns.Cast<ColumnHeader>().Select(c => c.Text).ToArray();

    private static string ColumnsFile(Harness h) => Path.Combine(h.Library.StoreFolder, ColumnLayout.FileName);

    /// <summary>The header menu as a right-click on the header opens it: its ticks set first.</summary>
    private static Dictionary<string, ToolStripMenuItem> OpenColumnMenu(MainForm form)
    {
        typeof(ToolStripDropDown).GetMethod("OnOpening", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form._columnMenu, new object[] { new System.ComponentModel.CancelEventArgs() });
        return form._columnMenu.Items.Cast<ToolStripMenuItem>().ToDictionary(i => i.Text!);
    }

    private static string Cell(MainForm form, int row, string heading) =>
        form._queueList.Items[row].SubItems[Array.IndexOf(Headings(form), heading)].Text;

    [Fact]
    public void ALibraryWithNoColumnsFile_ShowsTheDefaultFour_AndTheRowButton() => RunSta(() =>
    {
        using var h = new Harness();

        Assert.False(File.Exists(ColumnsFile(h)));
        Assert.Equal(["#", "Title", "Artist", "Length", ""], Headings(h.Form));
        var menu = OpenColumnMenu(h.Form);
        Assert.Equal(["#", "Title", "Artist", "Album", "Length", "Format", "Replay gain", "Path"], menu.Keys);
        Assert.Equal([true, true, true, false, true, false, false, false], menu.Values.Select(i => i.Checked));
        Assert.False(menu["#"].Enabled); // always shown
    });

    [Fact]
    public void TheHeaderMenu_TicksReplayGainAndAlbum_AndBothShowATaggedFlacsValues() => RunSta(() =>
    {
        using var h = new Harness();
        h.Form.AddPaths([TestAudio.WriteTaggedFlac("tagged", "First Album", "-7.20 dB"), TestAudio.WriteDcWav("plain.wav", 1, 0.2)]);

        OpenColumnMenu(h.Form)["Replay gain"].PerformClick();
        OpenColumnMenu(h.Form)["Album"].PerformClick();

        Assert.Equal(["#", "Title", "Artist", "Album", "Length", "Replay gain", ""], Headings(h.Form));
        Assert.Equal("First Album", Cell(h.Form, 0, "Album"));
        Assert.Equal("−7.2 dB", Cell(h.Form, 0, "Replay gain"));
        Assert.Equal("", Cell(h.Form, 1, "Replay gain")); // untagged: blank
        Assert.True(OpenColumnMenu(h.Form)["Album"].Checked);
        Assert.True(ColumnLayout.Load(ColumnsFile(h))[QueueColumn.Album].Visible); // written at once

        OpenColumnMenu(h.Form)["Album"].PerformClick();

        Assert.Equal(["#", "Title", "Artist", "Length", "Replay gain", ""], Headings(h.Form));
        Assert.False(ColumnLayout.Load(ColumnsFile(h))[QueueColumn.Album].Visible);
    });

    [Fact]
    public void TickingPath_ShowsEachRowsFullPath_AndFormatItsRateAndDepth() => RunSta(() =>
    {
        using var h = new Harness();
        var flac = TestAudio.WriteTaggedFlac("tagged", null, null);
        h.Form.AddPaths([flac]);

        OpenColumnMenu(h.Form)["Path"].PerformClick();
        OpenColumnMenu(h.Form)["Format"].PerformClick();

        Assert.Equal(flac, Cell(h.Form, 0, "Path"));
        Assert.Equal("44.1 kHz / 16-bit", Cell(h.Form, 0, "Format"));
    });

    [Fact]
    public void AColumnShownOrHidden_KeepsTheRowsSelection_AndTheRowButtonLast() => RunSta(() =>
    {
        using var h = new Harness();
        ThreeQueued(h);
        _ = h.Form._queueList.Handle;
        SelectRows(h.Form._queueList, 1);
        h.Engine.Author(h.Engine.AdjacenciesOf(h.Engine.View.Queue[0].Id).Next!, FadeOutAt(1));

        OpenColumnMenu(h.Form)["Path"].PerformClick();

        Assert.Equal([1], h.Form._queueList.SelectedIndices.Cast<int>());
        Assert.Equal(h.Form._queueList.Columns.Count - 1, h.Form.EditColumn);
        Assert.Equal(["✎", "✎", ""], EditGlyphs(h.Form._queueList));
        Assert.True(h.Form._queueList.Items[0].SubItems[h.Form.EditColumn].Font.Bold); // still styled
        ClickEditButton(h.Form._queueList, 1);
        Assert.NotNull(h.Form._editor.Adjacency);
    });

    [Fact]
    public void AlbumMovedBeforeArtist_AndResized_ComesBackAfterSwitchingLibrariesAndBack() => RunSta(() =>
    {
        using var h = new Harness();
        _ = h.Form._queueList.Handle; // a width set raises its event only on a list with a handle
        var second = Path.Combine(h.TempDir, "Second library");
        Directory.CreateDirectory(second);
        OpenColumnMenu(h.Form)["Album"].PerformClick();

        h.Form.MoveColumn(QueueColumn.Album, 2); // dropped on Artist
        h.Form._queueList.Columns[2].Width = 150;
        h.Engine.OpenLibrary(second);

        Assert.Equal(["#", "Title", "Artist", "Length", ""], Headings(h.Form)); // the other library's: the default
        Assert.Equal(["#", "Title", "Album", "Artist", "Length"], // written as it was left
            ColumnLayout.Load(ColumnsFile(h)).Shown.Select(c => Presenter.ColumnHeading(c.Column)));
        Assert.Equal(150, ColumnLayout.Load(ColumnsFile(h))[QueueColumn.Album].Width);

        h.Engine.OpenLibrary(h.TempDir);

        Assert.Equal(["#", "Title", "Album", "Artist", "Length", ""], Headings(h.Form));
        Assert.Equal(150, h.Form._queueList.Columns[2].Width);
        Assert.False(File.Exists(Path.Combine(second, Library.StoreFolderName, ColumnLayout.FileName))); // never changed there
    });

    [Fact]
    public void WidthsTheFitGave_AreNotTheUsers_EvenWhenTheListReplaysThemAsItsHandleIsMade() => RunSta(() =>
    {
        // Fitted before the list has a handle, the title and artist take what is left; making the
        // handle replays every width as a change. None of that is a width the user dragged.
        using var h = new Harness();
        _ = h.Form._queueList.Handle;
        Assert.NotEqual(320, h.Form._queueList.Columns[1].Width); // fitted, not the layout's

        OpenColumnMenu(h.Form)["Album"].PerformClick(); // writes the file

        var written = ColumnLayout.Load(ColumnsFile(h));
        Assert.Equal(ColumnLayout.Default[QueueColumn.Title].Width, written[QueueColumn.Title].Width);
        Assert.Equal(ColumnLayout.Default[QueueColumn.Artist].Width, written[QueueColumn.Artist].Width);
    });

    [Fact]
    public void AHeaderDroppedOnNumberOrTheRowButton_LandsBesideIt() => RunSta(() =>
    {
        using var h = new Harness();

        h.Form.MoveColumn(QueueColumn.Length, 0);
        Assert.Equal(["#", "Length", "Title", "Artist", ""], Headings(h.Form));

        h.Form.MoveColumn(QueueColumn.Title, 4);
        Assert.Equal(["#", "Length", "Artist", "Title", ""], Headings(h.Form));

        h.Form.MoveColumn(QueueColumn.Number, 3);
        Assert.Equal(["#", "Length", "Artist", "Title", ""], Headings(h.Form));
    });

    [Fact]
    public void ACorruptColumnsFile_IsTheDefaultFour() => RunSta(() =>
    {
        using var h = new Harness();
        var second = Path.Combine(h.TempDir, "Second library");
        Directory.CreateDirectory(Path.Combine(second, Library.StoreFolderName));
        File.WriteAllText(Path.Combine(second, Library.StoreFolderName, ColumnLayout.FileName), "{ not json");

        h.Engine.OpenLibrary(second);

        Assert.Equal(["#", "Title", "Artist", "Length", ""], Headings(h.Form));
    });

    // ── Columns that give way (phase 3 issue 01) ──────────────────────────────────────────

    private static readonly (QueueColumn Column, int Floor)[] GiveWayOrder =
        [(QueueColumn.Path, 80), (QueueColumn.Album, 60), (QueueColumn.Artist, 60)];

    private static int WidthOf(MainForm form, QueueColumn column) =>
        form._queueList.Columns.Cast<ColumnHeader>().First(c => Equals(c.Tag, column)).Width;

    /// <summary>Every column shown: Album, Format, Replay gain and Path ticked beside the default four.</summary>
    private static void ShowAllEight(Harness h)
    {
        _ = h.Form._queueList.Handle;
        foreach (var heading in new[] { "Album", "Format", "Replay gain", "Path" })
        {
            OpenColumnMenu(h.Form)[heading].PerformClick();
        }
    }

    /// <summary>Resizes the window and lets the list fit its columns, which it does once it has
    /// finished sizing.</summary>
    private static void SizeWindow(MainForm form, int width)
    {
        form.Width = width;
        Application.DoEvents();
    }

    private static int ColumnsAndScrollBar(MainForm form) =>
        form._queueList.Columns.Cast<ColumnHeader>().Sum(c => c.Width) + SystemInformation.VerticalScrollBarWidth;

    [Fact]
    public void AllEightShown_AtTheDefaultSize_FitTheList_WithPathAlbumAndArtistGivingWay() => RunSta(() =>
    {
        using var h = new Harness();
        ShowAllEight(h);
        SizeWindow(h.Form, 1240);

        Assert.True(ColumnsAndScrollBar(h.Form) <= h.Form._queueList.ClientSize.Width, "the row button must be in view");
        foreach (var (column, floor) in GiveWayOrder)
        {
            Assert.InRange(WidthOf(h.Form, column), floor, ColumnLayout.Default[column].Width - 1);
        }
        Assert.True(WidthOf(h.Form, QueueColumn.Title) >= 100);
        foreach (var column in new[] { QueueColumn.Number, QueueColumn.Length, QueueColumn.Format, QueueColumn.ReplayGain })
        {
            Assert.Equal(ColumnLayout.Default[column].Width, WidthOf(h.Form, column)); // never shrinks
        }
    });

    [Fact]
    public void WideningTheWindow_GivesArtistThenAlbumThenPathTheirWidthsBack_AndNeverWritesThem() => RunSta(() =>
    {
        using var h = new Harness();
        ShowAllEight(h);
        SizeWindow(h.Form, 1240);
        var written = File.ReadAllText(ColumnsFile(h));

        for (var width = 1240; width <= 2000; width += 20)
        {
            SizeWindow(h.Form, width);
            Assert.True(ColumnsAndScrollBar(h.Form) <= h.Form._queueList.ClientSize.Width, $"fits at {width}");
            // A column takes width back only once every column after it in the order has all its own.
            for (var i = 1; i < GiveWayOrder.Length; i++)
            {
                if (WidthOf(h.Form, GiveWayOrder[i].Column) < ColumnLayout.Default[GiveWayOrder[i].Column].Width)
                {
                    Assert.Equal(GiveWayOrder[i - 1].Floor, WidthOf(h.Form, GiveWayOrder[i - 1].Column));
                }
            }
        }

        foreach (var (column, _) in GiveWayOrder)
        {
            Assert.Equal(ColumnLayout.Default[column].Width, WidthOf(h.Form, column));
        }
        Assert.Equal(written, File.ReadAllText(ColumnsFile(h)));
    });

    [Fact]
    public void AllEightAtTheMinimumWidth_HoldTheirFloors_AndTheListScrollsSideways() => RunSta(() =>
    {
        using var h = new Harness();
        ShowAllEight(h);
        SizeWindow(h.Form, h.Form.MinimumSize.Width);

        foreach (var (column, floor) in GiveWayOrder)
        {
            Assert.Equal(floor, WidthOf(h.Form, column));
        }
        Assert.Equal(100, WidthOf(h.Form, QueueColumn.Title));
        Assert.True(ColumnsAndScrollBar(h.Form) > h.Form._queueList.ClientSize.Width);
    });

    [Fact]
    public void TheDefaultFour_FillTheListAsBefore_ArtistAloneGivingWayAtTheMinimum() => RunSta(() =>
    {
        using var h = new Harness();
        _ = h.Form._queueList.Handle;

        SizeWindow(h.Form, 1240);
        Assert.Equal(ColumnLayout.Default[QueueColumn.Artist].Width, WidthOf(h.Form, QueueColumn.Artist));
        Assert.Equal(h.Form._queueList.ClientSize.Width, ColumnsAndScrollBar(h.Form));

        SizeWindow(h.Form, h.Form.MinimumSize.Width);
        Assert.Equal(100, WidthOf(h.Form, QueueColumn.Title));
        Assert.InRange(WidthOf(h.Form, QueueColumn.Artist), 60, ColumnLayout.Default[QueueColumn.Artist].Width - 1);
        Assert.Equal(h.Form._queueList.ClientSize.Width, ColumnsAndScrollBar(h.Form));
    });

    [Fact]
    public void APathTheUserMadeNarrowerThanItsFloor_IsNeverWidenedToIt() => RunSta(() =>
    {
        using var h = new Harness();
        ShowAllEight(h);
        var path = h.Form._queueList.Columns.Cast<ColumnHeader>().First(c => Equals(c.Tag, QueueColumn.Path));

        path.Width = 50; // dragged
        SizeWindow(h.Form, h.Form.MinimumSize.Width);

        Assert.Equal(50, WidthOf(h.Form, QueueColumn.Path));
    });
}
