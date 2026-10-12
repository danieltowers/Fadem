using System.Reflection;
using System.Drawing;
using System.Windows.Forms;
using Fadem.Core.Persistence;
using Fadem.Shell;
using MusicBeePlugin;

namespace Fadem.MusicBee.Tests;

/// <summary>The plugin queue's columns, as the App's: a right-click on the header ticks which
/// show, a header drags to a new place, and the layout is the plugin's own in each library. The
/// speaker at the far left and the seam buttons at the far right stay where they are.</summary>
public sealed partial class PluginTests
{
    private string PluginColumnsFile => Path.Combine(LibraryFolder, Library.StoreFolderName, PluginView.ColumnsFileName);

    private PluginView ColumnsView(Panel panel, Action? beforeStart = null)
    {
        AuthorLibrary();
        beforeStart?.Invoke();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);
        view.Render();
        return view;
    }

    /// <summary>The headings, leaving out the speaker's: the last, shown first.</summary>
    private static string[] Headings(PluginView view) =>
        view._queueList.Columns.Cast<ColumnHeader>().Take(view._queueList.Columns.Count - 1).Select(c => c.Text).ToArray();

    /// <summary>The header menu as a right-click on the header opens it: its ticks set first.</summary>
    private static Dictionary<string, ToolStripMenuItem> OpenColumnMenu(PluginView view)
    {
        typeof(ToolStripDropDown).GetMethod("OnOpening", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(view._columns.Menu, new object[] { new System.ComponentModel.CancelEventArgs() });
        return view._columns.Menu.Items.Cast<ToolStripMenuItem>().ToDictionary(i => i.Text!);
    }

    private static void AssertEndsFixed(PluginView view)
    {
        var columns = view._queueList.Columns;
        Assert.Equal(0, columns[columns.Count - 1].DisplayIndex); // the speaker, at the far left
        Assert.Equal(columns.Count - 2, view.EditColumn); // the seam buttons, after every column shown
        Assert.Equal(columns.Count - 1, columns[view.EditColumn].DisplayIndex); // at the far right
        Assert.Equal("⋈", view._queueList.Items[0].SubItems[view.EditColumn].Text);
    }

    [Fact]
    public void TheHeaderMenu_ShowsAndHidesColumns_AndThePluginsOwnFileKeepsThem() => RunSta(() =>
    {
        using var panel = new Panel { Size = new Size(900, 500) };
        var view = ColumnsView(panel);
        var menu = OpenColumnMenu(view);
        Assert.Equal(new[] { "#", "Title", "Artist", "Album", "Length", "Format", "Track gain", "Path" }, menu.Keys);
        Assert.Equal(new[] { true, true, true, false, true, false, true, false }, menu.Values.Select(i => i.Checked));
        Assert.False(menu["#"].Enabled); // always shown

        OpenColumnMenu(view)["Album"].PerformClick();
        OpenColumnMenu(view)["Track gain"].PerformClick();

        Assert.Equal(new[] { "#", "Title", "Artist", "Album", "Length", "" }, Headings(view));
        Assert.Equal("1", view._queueList.Items[0].Text);
        Assert.Equal("", view._queueList.Items[0].SubItems[3].Text); // the album: none tagged
        AssertEndsFixed(view);
        var saved = ColumnLayout.Load(PluginColumnsFile);
        Assert.True(saved[QueueColumn.Album].Visible);
        Assert.False(saved[QueueColumn.ReplayGain].Visible);
        // The App's layout in the same library is its own.
        Assert.False(File.Exists(Path.Combine(LibraryFolder, Library.StoreFolderName, ColumnLayout.FileName)));
    });

    [Fact]
    public void AHeaderDraggedToANewPlace_MovesItsColumn_ButNeverBeforeNumberOrPastTheButton() => RunSta(() =>
    {
        using var panel = new Panel { Size = new Size(900, 500) };
        var view = ColumnsView(panel);
        var title = view._queueList.Items[0].SubItems[1].Text;

        view._columns.Move(QueueColumn.Length, 1);

        Assert.Equal(new[] { "#", "Length", "Title", "Artist", "Track gain", "" }, Headings(view));
        Assert.Equal(title, view._queueList.Items[0].SubItems[2].Text); // each cell moved with its column
        AssertEndsFixed(view);

        view._columns.Move(QueueColumn.Title, 0); // dropped on #, or the speaker: lands after #
        view._columns.Move(QueueColumn.Artist, 99); // dropped on the seam buttons: lands before it
        view._columns.Move(QueueColumn.Number, 3); // # stays

        Assert.Equal(new[] { "#", "Title", "Length", "Track gain", "Artist", "" }, Headings(view));
        AssertEndsFixed(view);
        Assert.Equal(
            new[] { QueueColumn.Number, QueueColumn.Title, QueueColumn.Length, QueueColumn.ReplayGain, QueueColumn.Artist },
            ColumnLayout.Load(PluginColumnsFile).Shown.Select(c => c.Column));
    });

    [Fact]
    public void ALibrarysSavedLayout_IsTheQueuesColumnsAtTheFirstRender() => RunSta(() =>
    {
        using var panel = new Panel { Size = new Size(900, 500) };

        var view = ColumnsView(panel, () =>
            ColumnLayout.Default.Toggle(QueueColumn.Format).Move(QueueColumn.Format, 1).Save(PluginColumnsFile));

        Assert.Equal(new[] { "#", "Format", "Title", "Artist", "Length", "" }, Headings(view));
        Assert.NotEqual("", view._queueList.Items[0].SubItems[1].Text); // the WAV's format
        AssertEndsFixed(view);
    });
}
