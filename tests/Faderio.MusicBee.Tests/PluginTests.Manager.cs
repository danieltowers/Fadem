using System.Drawing;
using System.Windows.Forms;
using Faderio.Core.Domain;
using Faderio.Core.Persistence;
using Faderio.Core.Playback;
using Faderio.Core.Queue;
using Faderio.Core.Tests;
using Faderio.Shell;

namespace Faderio.MusicBee.Tests;

/// <summary>
/// The transition manager inside MusicBee (phase 5 issue 12): the shared manager owned by the
/// plugin's view, opened from the queue's menu on the selected row's track, previewing through
/// the plugin's output with MusicBee's player stopped first, handing a double-click to the
/// view's editor, and listing a gone track as standalone faderio lists it.
/// </summary>
public sealed partial class PluginTests
{
    /// <summary>The manager the view showed for the queue's Transitions… item, with the row at
    /// <paramref name="row"/> selected, or none.</summary>
    private static TransitionManagerForm OpenManager(PluginView view, int? row)
    {
        TransitionManagerForm? shown = null;
        view.ShowTransitionManager = form => shown = (TransitionManagerForm)form;
        view._queueList.SelectedIndices.Clear();
        if (row is { } index)
        {
            view._queueList.Items[index].Selected = true;
        }
        Open(view._queueList.ContextMenuStrip!);
        Assert.True(view._openTransitions.Enabled);
        view._openTransitions.PerformClick();
        return Assert.IsType<TransitionManagerForm>(shown);
    }

    private static string[] GridRow(TransitionManagerForm manager, int row) =>
        Enumerable.Range(0, TransitionManagerForm.PreviewColumn).Select(c => (string)manager._grid.Rows[row].Cells[c].Value).ToArray();

    [Fact]
    public void TheQueuesMenu_OpensTheManagerOnTheSelectedRowsTrack_ElseOnTheStoresFirst_OneManagerReused() => RunSta(() =>
    {
        var (a, b) = AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        using var panel = new Panel { Size = new Size(900, 500) };
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);
        view.Render();

        var manager = OpenManager(view, row: 1); // tone b: incoming from a

        Assert.Equal(b, manager.PickedTrack!.FilePath);
        Assert.Equal(2, manager._track.Items.Count);
        Assert.Equal(1, manager._grid.Rows.Count);
        // The crossfade as standalone faderio left it in the store, read back whole.
        Assert.Equal(["←", Path.GetFileNameWithoutExtension(a), "1.5 | –", "1 | –", "0 | –", "1 | –", "",
            "fade-out at 1.5 s for 1 s, fade-in from 0 s over 1 s", ""], GridRow(manager, 0));
        Assert.Equal(Presenter.ManagerLegend("Untitled", anyTracks: true), manager._legend.Text);

        var again = OpenManager(view, row: null);

        Assert.Same(manager, again);
        Assert.Equal(a, again.PickedTrack!.FilePath); // the store's first track, by title
        Assert.Equal("→", GridRow(again, 0)[0]);
    });

    [Fact]
    public void PreviewFromTheManager_StopsMusicBeesPlayerFirst_PlaysThroughTheOutput_AndHoldsTheQueue() => RunSta(() =>
    {
        AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        using var panel = new Panel { Size = new Size(900, 500) };
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);
        view.Render();
        var manager = OpenManager(view, row: 0);
        var outputAtPlayerStop = new List<(Transport, int)>();
        _musicBee.PlayerStopped = () => outputAtPlayerStop.Add((_output.State, _output.ReadCallCount));
        var rowsBefore = _plugin.Host!.Engine.View.Queue.Select(r => r.Id).ToList();

        manager.PreviewAt(0, TransitionLayer.Playlist);
        _output.Advance(4410);

        Assert.Equal(new[] { (Transport.Stopped, 0) }, outputAtPlayerStop);
        Assert.Equal(Transport.Playing, _output.State);
        Assert.True(_output.FramesRendered > 0);
        var held = _plugin.Host.Engine.View;
        Assert.NotNull(held.Preview);
        Assert.Equal(Transport.Stopped, held.Transport); // the queue, held where it was: never started
        Assert.Null(held.Attended);
        Assert.Equal(rowsBefore, held.Queue.Select(r => r.Id));
        view.Render();
        Assert.True(manager._stopPreview.Enabled);
        Assert.Equal("Pre-roll · outgoing 0:00", manager._previewStatus.Text); // 1.5 s − 5 s, clamped to the start

        Click(manager._stopPreview);

        Assert.Equal(Transport.Stopped, _output.State);
        Assert.Null(_plugin.Host.Engine.View.Preview);
        Assert.Equal(rowsBefore, _plugin.Host.Engine.View.Queue.Select(r => r.Id));
        Assert.Equal(1, _musicBee.Calls.Count(c => c == "Player_Stop"));
    });

    [Fact]
    public void PreviewGlobalFromTheManager_OverAPlayingQueue_DoesNotStopMusicBeesPlayerAgain() => RunSta(() =>
    {
        AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        using var panel = new Panel { Size = new Size(900, 500) };
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);
        view.Render();
        Click(view._playPause);
        _output.Advance(4410);
        Assert.Equal(1, _musicBee.Calls.Count(c => c == "Player_Stop"));
        var manager = OpenManager(view, row: 0);

        manager.PreviewAt(0, TransitionLayer.Global);
        _output.Advance(441);

        Assert.NotNull(_plugin.Host!.Engine.View.Preview);
        Assert.Equal(1, _musicBee.Calls.Count(c => c == "Player_Stop"));
        Assert.Equal(Transport.Playing, _output.State);
    });

    [Fact]
    public void ADoubleClickOnARow_OpensTheViewsEditorOnIt_InASidebarAsThePage() => RunSta(() =>
    {
        var (a, b) = AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        using var panel = new Panel { Size = new Size(285, 330) };
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);
        view.Render();
        var manager = OpenManager(view, row: 0);
        Assert.False(view._editor.Visible);

        manager.EditAt(0);

        Assert.Equal(new Adjacency(TrackOf(a), TrackOf(b)), view._editor.Adjacency);
        Assert.Equal("1.5", view._editor._fadeOutPoint.Text);
        Assert.True(view._editor.Visible);
        Assert.True(view._closeEditor.Visible);
        Assert.False(view._queueList.Visible);
        Assert.False(manager.IsDisposed); // the manager stays, beside the editor
    });

    // ── A gone track: auto-organise moved its file (ADR 0005, phase 3 issue 02) ─────────

    /// <summary>A third tone with a → c authored through the library's store, then moved out
    /// from under it as MusicBee's auto-organise would move it. Returns the path the store names.</summary>
    private string AuthorThenMoveAway(string a)
    {
        var c = TestAudio.WriteToneWav("musicbee-c.wav", 4, 880);
        Library.Open(LibraryFolder).Transitions!.Upsert(a, c, Crossfade);
        var organised = Path.Combine(_dir, "organised");
        Directory.CreateDirectory(organised);
        File.Move(c, Path.Combine(organised, Path.GetFileName(c)));
        return c;
    }

    private static int RowOfOther(TransitionManagerForm manager, string heading) =>
        Enumerable.Range(0, manager._grid.Rows.Count).Single(r => GridRow(manager, r)[1] == heading);

    [Fact]
    public void AGoneTrack_ListsGreyedWithItsPath_AndItsEditorCanRemoveButNotChange() => RunSta(() =>
    {
        var (a, _) = AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        var c = AuthorThenMoveAway(a);
        Start();
        Launched();
        using var panel = new Panel { Size = new Size(900, 500) };
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);
        view.Render();

        var manager = OpenManager(view, row: 0); // a: → b, → ⚠ c
        var row = manager._grid.Rows[RowOfOther(manager, "⚠ " + c)];

        Assert.Equal(2, manager._grid.Rows.Count);
        Assert.Equal("→", row.Cells[0].Value);
        Assert.Same(view.Palette, Faderio.Shell.Theme.StudioTheme.PaletteOf(manager)); // in the skin's palette (studio theme issue 06)
        Assert.Equal(view.Palette.Dim, row.DefaultCellStyle.ForeColor);
        Assert.Equal($"Not found where the store says: {c}", row.Cells[1].ToolTipText);
        Assert.Contains("⚠ " + c, manager._track.Items.Cast<object>().Select(i => i.ToString()));

        manager.EditAt(row.Index);

        var editor = view._editor;
        Assert.True(editor.Adjacency!.Incoming.Gone);
        Assert.Equal($"{Path.GetFileName(c)} is missing: this transition can be removed, not changed.", editor._missing.Text);
        Assert.Equal("1.5", editor._fadeOutPoint.Text); // what the store holds, to read
        Assert.True(editor._fadeOutPoint.ReadOnly);
        foreach (var button in new[] { editor._preview, editor._render })
        {
            Assert.False(button.Enabled, button.Text);
        }
        Assert.True(editor._remove.Enabled);

        Click(editor._remove);
        _ui.Pump();
        view.Render();

        Assert.Empty(_ui.Messages);
        Assert.Null(Library.Open(LibraryFolder).Transitions!.Get(a, c));
        Assert.Equal(1, manager._grid.Rows.Count);
        Assert.DoesNotContain("⚠ " + c, manager._track.Items.Cast<object>().Select(i => i.ToString()));
        Assert.False(editor.HasUnsavedEdits); // leaving asks nothing: it could never be saved
    });

    [Fact]
    public void ATransitionSavedInTheEditor_WhileTheManagerIsOpen_AppearsInIt_AndThePanelsDisposalTakesTheManager() => RunSta(() =>
    {
        var (a, b) = AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        var panel = new Panel { Size = new Size(900, 500) };
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);
        view.Render();
        var manager = OpenManager(view, row: 1); // b: ← a only
        Assert.Equal(1, manager._grid.Rows.Count);

        _plugin.Host!.Engine.SetRepeat(RepeatMode.One);
        view.Render();
        view._queueList.SelectedIndices.Clear();
        view._queueList.Items[1].Selected = true;
        Open(view._queueList.ContextMenuStrip!);
        view._editMenu.Self.PerformClick(); // b → b
        view._editor._fadeOutPoint.Text = "2";
        view._editor.Commit();
        _ui.Pump();
        view.Render();

        Assert.StartsWith("Saved ", view._status.Text);
        Assert.Equal(b, manager.PickedTrack!.FilePath);
        Assert.Equal(new[] { ("→", Path.GetFileNameWithoutExtension(b)), ("←", Path.GetFileNameWithoutExtension(a)) },
            Enumerable.Range(0, manager._grid.Rows.Count).Select(r => (GridRow(manager, r)[0], GridRow(manager, r)[1])));

        panel.Dispose();

        Assert.True(view.IsDisposed);
        Assert.True(manager.IsDisposed);
    });
}
