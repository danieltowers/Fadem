using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Faderio.Core.Domain;
using Faderio.Core.Persistence;
using Faderio.Core.Playback;
using Faderio.Core.Queue;
using Faderio.Shell;

namespace Faderio.MusicBee.Tests;

/// <summary>
/// The transition editor and preview inside MusicBee (phase 5 issue 11): the shared editor
/// composed into the plugin's view, opened from the queue's ✎, previewing through the plugin's
/// output with MusicBee's player stopped first, and the store it writes read back as standalone
/// faderio reads it.
/// </summary>
public sealed partial class PluginTests
{
    [Fact]
    public void ThePencilOnARow_OpensTheEditorOnThisToNext_WithTheAuthoredCrossfade() => RunSta(() =>
    {
        var (a, b) = AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        using var panel = new Panel { Size = new Size(900, 500) };
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);
        view.Render();

        Assert.Equal("✎", view._queueList.Items[0].SubItems[view.EditColumn].Text);
        Assert.Equal("", view._queueList.Items[1].SubItems[view.EditColumn].Text); // the last row has no next
        Assert.Equal("No boundary selected", view._editor._title.Text);

        view.EditTransitionAt(0);
        view.Render();

        var adjacency = new Adjacency(TrackOf(a), TrackOf(b));
        Assert.Equal(adjacency, view._editor.Adjacency);
        Assert.Equal(adjacency.Name, view._editor._title.Text);
        // As standalone faderio left it in the store, loaded exactly.
        Assert.Equal("1.5", view._editor._fadeOutPoint.Text);
        Assert.Equal("1", view._editor._fadeOutDuration.Text);
        Assert.Equal("0", view._editor._fadeInPoint.Text);
        Assert.Equal("1", view._editor._fadeInDuration.Text);
        Assert.True(view._editor._save.Enabled);
    });

    [Fact]
    public void PreviewFromTheEditor_StopsMusicBeesPlayerFirst_PlaysThroughTheOutput_AndHoldsTheQueue() => RunSta(() =>
    {
        AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        using var panel = new Panel { Size = new Size(900, 500) };
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);
        view.Render();
        view.EditTransitionAt(0);
        var outputAtPlayerStop = new List<(Transport, int)>();
        _musicBee.PlayerStopped = () => outputAtPlayerStop.Add((_output.State, _output.ReadCallCount));
        var rowsBefore = _plugin.Host!.Engine.View.Queue.Select(r => r.Id).ToList();

        Click(view._editor._preview);
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
        Assert.Equal("Preview again", view._editor._preview.Text);
        Assert.True(view._editor._stopPreview.Enabled);
        Assert.True(view._editor._previewStatus.Visible);

        Click(view._editor._stopPreview);

        Assert.Equal(Transport.Stopped, _output.State);
        Assert.Null(_plugin.Host.Engine.View.Preview);
        Assert.Null(_plugin.Host.Engine.View.Attended);
        Assert.Equal(rowsBefore, _plugin.Host.Engine.View.Queue.Select(r => r.Id));
        Assert.Equal(1, _musicBee.Calls.Count(c => c == "Player_Stop"));
    });

    [Fact]
    public void PreviewOverAPlayingQueue_OrOverARunningPreview_DoesNotStopMusicBeesPlayerAgain() => RunSta(() =>
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
        view.EditTransitionAt(0);

        Click(view._editor._preview);
        _output.Advance(441);
        Assert.NotNull(_plugin.Host!.Engine.View.Preview);
        Click(view._editor._preview); // again, over the running preview
        _output.Advance(441);

        Assert.Equal(1, _musicBee.Calls.Count(c => c == "Player_Stop"));
        Assert.Equal(Transport.Playing, _output.State);
    });

    [Fact]
    public void ARefusedDraft_IsANoticeInTheStrip_NotABox() => RunSta(() =>
    {
        AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        using var panel = new Panel { Size = new Size(900, 500) };
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);
        view.Render();
        view.EditTransitionAt(0);

        view._editor._fadeOutDuration.Text = "abc";
        Click(view._editor._save);
        _ui.Pump();

        Assert.Empty(_ui.Messages);
        Assert.StartsWith("Not saved:", view._status.Text);
        Assert.Equal("1 notice", view._noticeCount.Text);
        Assert.True(view._editor.ReasonFor(TransitionField.FadeOutDuration).Visible);
    });

    [Fact]
    public void InASidebar_TheEditorIsHiddenUntilAnAdjacencyOpens_ThenTakesTheListsPlaceUnderItsCloseBar() => RunSta(() =>
    {
        AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        using var panel = new Panel { Size = new Size(285, 330) }; // MusicBee's sidebar as shipped, under two other panels
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);
        view.Render();

        Assert.False(view._editor.Visible);
        Assert.False(view._closeEditor.Visible);
        Assert.True(view._queueList.Visible);

        view.EditTransitionAt(0);

        Assert.True(view._editor.Visible);
        Assert.Equal(DockStyle.Fill, view._editor.Dock);
        Assert.True(view._closeEditor.Visible);
        Assert.False(view._queueList.Visible);
        Assert.False(view._playlists.Visible);
        Assert.True(view._closeEditor.Parent!.Bottom <= view._editor.Top, "the Close bar is not above the editor");
        Assert.True(view._editor.Top >= view._seek.Parent!.Bottom, "the editor covers the transport");
        Assert.True(view._editor.Height >= 150, $"the editor has only {view._editor.Height} px");
        // Nothing in the editor is cut off at this width: its button rows wrap instead.
        Assert.True(view._editor._render.Right <= view._editor._render.Parent!.ClientSize.Width, "Render is off the edge");
        Assert.True(view._editor._remove.Right <= view._editor._remove.Parent!.ClientSize.Width, "Remove transition is off the edge");

        view.CloseEditor();

        Assert.False(view._editor.Visible);
        Assert.False(view._closeEditor.Visible);
        Assert.True(view._queueList.Visible);
        Assert.True(view._playlists.Visible);
        Assert.Null(view._editor.Adjacency);
        Assert.Equal("No boundary selected", view._editor._title.Text);
    });

    [Fact]
    public void InAMainPanel_TheEditorIsDockedRight_FromTheStart_AndMovesWithTheWidth() => RunSta(() =>
    {
        AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        using var panel = new Panel { Size = new Size(900, 500) };
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);

        Assert.True(view._editor.Visible);
        Assert.Equal(DockStyle.Right, view._editor.Dock);
        Assert.Equal(340, view._editor.Width);
        Assert.False(view._closeEditor.Visible);

        panel.Size = new Size(285, 900); // to the sidebar with nothing open: no page

        Assert.False(view._editor.Visible);
        Assert.True(view._queueList.Visible);

        panel.Size = new Size(900, 500); // back: the empty editor shows at the right again

        Assert.True(view._editor.Visible);
        Assert.Equal(DockStyle.Right, view._editor.Dock);
        view.EditTransitionAt(0);

        panel.Size = new Size(285, 900); // dragged to the sidebar, with an adjacency open

        Assert.Equal(DockStyle.Fill, view._editor.Dock);
        Assert.True(view._editor.Visible);
        Assert.True(view._closeEditor.Visible);
        Assert.False(view._queueList.Visible);

        panel.Size = new Size(900, 500);

        Assert.Equal(DockStyle.Right, view._editor.Dock);
        Assert.False(view._closeEditor.Visible);
        Assert.True(view._queueList.Visible);
    });

    [Fact]
    public void SwitchingPlaylist_WithUnsavedEdits_Asks_AndCancelStays() => RunSta(() =>
    {
        var (a, _) = AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        _plugin.Host!.Engine.NewPlaylist(); // "Untitled 2", with one row so it is kept
        _plugin.Host.Engine.Add(new[] { a }).Wait();
        _plugin.Host.Engine.OpenPlaylist("Untitled").Wait();
        Launched();
        using var panel = new Panel { Size = new Size(900, 500) };
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);
        view.Render();
        view.EditTransitionAt(0);
        view._editor._fadeOutDuration.Text = "2";
        var asked = new List<string>();
        view.AskUnsavedEdits = a => { asked.Add(a.Name); return UnsavedEdits.Cancel; };

        view._playlists.OpenAt(view._playlists._list.Items.Cast<ListViewItem>().ToList().FindIndex(i => i.Text == "Untitled 2"));

        Assert.Equal(new[] { view._editor.Adjacency!.Name }, asked);
        Assert.Equal("Untitled", _plugin.Host.Engine.View.OpenPlaylist);
        Assert.Equal("2", view._editor._fadeOutDuration.Text);
    });

    [Fact]
    public void ATransitionSavedInMusicBee_IsInTheLibrarysStore_AsStandaloneFaderioWouldAuthorIt() => RunSta(() =>
    {
        var (a, b) = AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        using var panel = new Panel { Size = new Size(900, 500) };
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);
        view.Render();
        view.EditTransitionAt(0);

        view._editor._fadeOutPoint.Text = "2.25";
        view._editor._fadeOutDuration.Text = "0.5";
        view._editor._fadeInPoint.Text = "0.75";
        view._editor._fadeInDuration.Text = "1.5";
        view._editor._curve.SelectedIndex = view._editor._curve.Items.Cast<object>().ToList().FindIndex(i => i.ToString() == Presenter.FmtCurve(FadeCurveKind.Linear));
        Click(view._editor._save);
        _ui.Pump();

        Assert.StartsWith("Saved ", view._status.Text);
        var stored = Library.Open(LibraryFolder).Transitions!.Get(a, b);
        Assert.Equal(new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(2.25), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(0.5),
            FadeInPoint = new FadePointSpec(TimeSpan.FromSeconds(0.75), FromEnd: false),
            FadeInDuration = TimeSpan.FromSeconds(1.5),
            Curve = FadeCurveKind.Linear,
        }, stored);
        view.Render();
        Assert.Equal("✎", view._queueList.Items[0].SubItems[view.EditColumn].Text);
        Assert.True(view._queueList.Items[0].SubItems[view.EditColumn].Font.Bold);
    });

    [Fact]
    public void TheQueuesMenu_EditsTheSelectedRowsAdjacencies_AndOffersThisToThisUnderRepeatOne() => RunSta(() =>
    {
        var (a, b) = AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        using var panel = new Panel { Size = new Size(900, 500) };
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);
        view.Render();
        view._queueList.Items[0].Selected = true;
        Open(view._queueList.ContextMenuStrip!);
        Assert.True(view._editMenu.Next.Enabled);
        Assert.False(view._editMenu.Previous.Enabled); // the first row has no previous

        view._editMenu.Next.PerformClick();

        Assert.Equal(new Adjacency(TrackOf(a), TrackOf(b)), view._editor.Adjacency);
        view.CloseEditor();
        view._queueList.Items[0].Selected = false;
        view._queueList.Items[1].Selected = true;

        Open(view._queueList.ContextMenuStrip!);

        Assert.False(view._editMenu.Next.Enabled); // the last row has no next
        Assert.True(view._editMenu.Previous.Enabled);
        Assert.False(view._editMenu.Self.Available);

        view._editMenu.Previous.PerformClick();

        Assert.Equal(new Adjacency(TrackOf(a), TrackOf(b)), view._editor.Adjacency);

        _plugin.Host!.Engine.SetRepeat(RepeatMode.One);
        Open(view._queueList.ContextMenuStrip!);

        Assert.True(view._editMenu.Self.Available);
        view._editMenu.Self.PerformClick();
        Assert.Equal(new Adjacency(TrackOf(b), TrackOf(b)), view._editor.Adjacency);
    });

    /// <summary>Raises the menu's Opening, as showing it would, without showing it.</summary>
    private static void Open(ContextMenuStrip menu) =>
        typeof(ToolStripDropDown).GetMethod("OnOpening", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(menu, new object[] { new System.ComponentModel.CancelEventArgs() });

    /// <summary>The queue's track for a file, as the engine published it.</summary>
    private Track TrackOf(string path) => _plugin.Host!.Engine.View.Queue.Single(r => r.Track.FilePath == path).Track;
}
