using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Drawing;
using System.Windows.Forms;
using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Tests;
using Faderio.Shell;
using MusicBeePlugin;

namespace Faderio.MusicBee.Tests;

/// <summary>
/// The plugin's dockable panel (phase 5 issue 10): MusicBee makes a panel and hands it to
/// <see cref="Plugin.OnDockablePanelCreated"/>, and what the plugin puts there is driven as the
/// listener would, against the fake output. Each test runs on an STA thread, as MusicBee's UI
/// thread is, with the recording context as that thread's queue.
/// </summary>
public sealed partial class PluginTests
{
    [Fact]
    public void Initialise_NamesThePanelElement_ForArrangePanels()
    {
        ChooseLibrary(LibraryFolder);

        var about = Start();

        Assert.Equal("faderio", about.TargetApplication);
    }

    [Fact]
    public void ThePanel_HoldsTheView_FillingIt_WithThePlaylistsTheQueueAndTheHost() => RunSta(() =>
    {
        var (a, b) = AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        var expected = Launched();
        using var panel = new Panel { Size = new Size(900, 500) };

        var height = _plugin.OnDockablePanelCreated(panel);

        Assert.Equal(-1, height); // MusicBee sizes the view to the panel
        var view = View(panel);
        Assert.Equal(DockStyle.Fill, view.Dock);
        view.Render();
        Assert.Equal(expected.Playlists, view._playlists._list.Items.Cast<ListViewItem>().Select(i => i.Text));
        Assert.Equal(new[] { "#", "Title", "Artist", "Length", "" }, view._queueList.Columns.Cast<ColumnHeader>().Select(c => c.Text));
        Assert.Equal(
            new[] { Path.GetFileNameWithoutExtension(a), Path.GetFileNameWithoutExtension(b) },
            view._queueList.Items.Cast<ListViewItem>().Select(i => i.SubItems[1].Text));
        Assert.Equal(new[] { "1", "2" }, view._queueList.Items.Cast<ListViewItem>().Select(i => i.Text));
        Assert.Equal("library · WASAPI shared at 44100 Hz", view._host.Text);
    });

    [Fact]
    public void AtStart_MusicBeeCallsFromAWorkerThread_AndTheViewIsMadeOnThePanelsThread() => RunSta(() =>
    {
        AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        using var panel = new Panel();
        _ = panel.Handle; // the UI thread's, as MusicBee's panel is
        int? height = null;
        var worker = new Thread(() => height = _plugin.OnDockablePanelCreated(panel));

        worker.Start();
        while (worker.IsAlive)
        {
            Application.DoEvents(); // MusicBee's UI thread pumps while its worker waits
            Thread.Sleep(5);
        }

        Assert.Equal(-1, height);
        var view = View(panel);
        Assert.False(view.InvokeRequired);
        Assert.Equal(2, view._queueList.Items.Count);
    });

    [Fact]
    public void ANotice_ReachesTheStripThroughMusicBeesMainWindow_NotTheStartUpThreadsContext() => RunSta(() =>
    {
        using var main = new Form();
        _ = main.Handle;
        _musicBee.MainWindow = main.Handle;
        AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        using var panel = new Panel();
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);

        _plugin.Host!.Engine.RenamePlaylist("Untitled", "a/b"); // refused: a notice
        Application.DoEvents();

        Assert.Empty(_ui.Posted); // nothing went to the context current at Initialise
        Assert.Equal(_plugin.Host.Notices.Latest!.Message, view._status.Text);
    });

    [Fact]
    public void InASidebar_ThePlaylistsSitAboveTheQueue_InAMainPanelBesideIt() => RunSta(() =>
    {
        AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        using var panel = new Panel { Size = new Size(285, 700) }; // MusicBee's sidebar, as shipped
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);

        Assert.Equal(DockStyle.Top, view._playlists.Dock);

        panel.Size = new Size(900, 500); // dragged to the main panel

        Assert.Equal(DockStyle.Left, view._playlists.Dock);
        Assert.Equal(200, view._playlists.Width);
    });

    [Fact]
    public void PlayFromTheView_StopsMusicBeesPlayerFirst_ThenPauseAndStopFollow() => RunSta(() =>
    {
        AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        using var panel = new Panel();
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);
        view.Render();
        var outputAtPlayerStop = new List<(Transport, int)>();
        _musicBee.PlayerStopped = () => outputAtPlayerStop.Add((_output.State, _output.ReadCallCount));

        Click(view._playPause);
        _output.Advance(4410);

        Assert.Equal(new[] { (Transport.Stopped, 0) }, outputAtPlayerStop);
        Assert.Equal(Transport.Playing, _output.State);
        view.Render();
        Assert.Equal("⏸", view._playPause.Text);
        Assert.Equal(RowMarksSounding, view._queueList.Items[0].SubItems[0].BackColor);

        Click(view._playPause);

        Assert.Equal(Transport.Paused, _output.State);
        Assert.Equal(1, _musicBee.Calls.Count(c => c == "Player_Stop")); // pausing starts nothing
        view.Render();
        Assert.Equal("▶", view._playPause.Text);

        Click(view._stop);

        Assert.Equal(Transport.Stopped, _output.State);
        Assert.False(_output.Disposed);
        view.Render();
        Assert.Equal("", view._attendedTrack.Text);
    });

    [Fact]
    public void ARowsDoubleClick_PlaysThatRow_AfterMusicBeesPlayerStops() => RunSta(() =>
    {
        var (_, b) = AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        using var panel = new Panel();
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);
        view.Render();

        view.PlayRowAt(1);
        _output.Advance(4410);

        Assert.Equal("Player_Stop", _musicBee.Calls.Last());
        Assert.Equal(Transport.Playing, _output.State);
        Assert.Equal(b, _plugin.Host!.Engine.View.Attended!.Row.Track.FilePath);
        view.Render();
        Assert.Equal(Path.GetFileNameWithoutExtension(b), view._attendedTrack.Text);
    });

    [Fact]
    public void SkipAndSeek_FromTheView_MoveThePlayhead() => RunSta(() =>
    {
        var (_, b) = AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        using var panel = new Panel { Size = new Size(900, 500) };
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);
        Click(view._playPause);
        _output.Advance(4410);

        Click(view._next);
        _output.Advance(441);

        Assert.Equal(b, _plugin.Host!.Engine.View.Attended!.Row.Track.FilePath);

        view.Render();
        Assert.True(view._seek.Enabled);
        // A click in the middle of the bar, which maps it to half the attended track's 4 s
        // (SeekBarTests) and asks the view for it.
        var travel = view._seek.Width - 2 * SeekBar.Inset;
        Assert.True(travel > 100, $"the bar has only {travel} px of travel");
        var middle = new MouseEventArgs(MouseButtons.Left, 1, SeekBar.Inset + travel / 2, 10, 0);
        Raise(view._seek, "OnMouseDown", middle);
        Raise(view._seek, "OnMouseUp", middle);
        _output.Advance(441);

        Assert.InRange(_plugin.Host.Engine.View.Attended!.Playhead.Milliseconds, 2000, 2100);
        view.Render();
        Assert.StartsWith("0:02 /", view._time.Text);
    });

    [Fact]
    public void TheJumps_AreStackedRightOfTheSeekBar_BackBelowForward() => RunSta(() =>
    {
        AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        using var panel = new Panel { Size = new Size(900, 500) };
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);
        view.Render();

        Assert.Same(view._forward5.Parent, view._back5.Parent);
        Assert.Equal(view._forward5.Left, view._back5.Left);
        Assert.True(view._back5.Top >= view._forward5.Bottom, "−5 s is not below +5 s");
        var jumps = view._back5.Parent!;
        Assert.True(view._seek.Right <= jumps.Left, "the jumps are not right of the seek bar");
        Assert.True(jumps.Bottom <= view._seek.Parent!.Height, "the jumps are cut off");
    });

    [Fact]
    public void ADragOnTheSeekBarsFadeOutStartMark_MovesTheAttendedRowsFade() => RunSta(() =>
    {
        var (a, b) = AuthorLibrary(); // a fades out at 1.5 s for 1 s
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        using var panel = new Panel { Size = new Size(900, 500) };
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);
        Click(view._playPause);
        _output.Advance(4410);
        view.Render();
        int X(double seconds) => SeekBar.Inset + (int)Math.Round(seconds / 4 * (view._seek.Width - 2 * SeekBar.Inset));

        Raise(view._seek, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, X(1.5), 1, 0));
        Raise(view._seek, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, X(1), 1, 0));

        var stored = _plugin.Host!.Engine.Transition(new Adjacency(TrackOf(a), TrackOf(b))).Global!;
        Assert.InRange(stored.FadeOutPoint!.Position.TotalSeconds, 0.95, 1.05);
        Assert.InRange((stored.FadeOutPoint.Position + stored.FadeOutDuration!.Value).TotalSeconds, 2.499, 2.501);
    });

    [Fact]
    public void ANotice_ShowsInTheStrip_WhileAViewIsAttached_AndIsABoxWithoutOne() => RunSta(() =>
    {
        AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        var panel = new Panel();
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);

        _plugin.Host!.Engine.RenamePlaylist("Untitled", "a/b"); // refused: a notice
        _ui.Pump();

        Assert.Empty(_ui.Messages);
        Assert.Equal(1, _plugin.Host.Notices.Entries.Count);
        var notice = _plugin.Host.Notices.Latest!.Message;
        Assert.Equal(notice, view._status.Text);
        Assert.Equal("1 notice", view._noticeCount.Text);

        panel.Dispose(); // the user removes the panel from the layout

        _plugin.Host.Engine.RenamePlaylist("Untitled", "a/b");
        _ui.Pump();

        Assert.Equal(new[] { notice }, _ui.Messages);
        Assert.Equal(2, _plugin.Host.Notices.Latest!.Count);
    });

    [Fact]
    public void APanelShowingSections_IsRemovedWithoutAnError() => RunSta(() =>
    {
        AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        var panel = new Panel { Size = new Size(600, 400) };
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);
        var engine = _plugin.Host!.Engine;
        Assert.NotEmpty(engine.View.Queue);
        engine.StartSection(engine.View.Queue[0].Id, "Intro");
        view.Render();
        Assert.True(view._queueList.ShowGroups);

        panel.Dispose(); // MusicBee closing, or the user removing the panel
    });

    [Fact]
    public void ANewPanel_ShowsTheNoticesAlreadyDelivered() => RunSta(() =>
    {
        ChooseLibrary(Path.Combine(_dir, "gone"));
        Start();
        _ui.Pump();
        var box = Assert.Single(_ui.Messages);
        using var panel = new Panel();

        _plugin.OnDockablePanelCreated(panel);

        var view = View(panel);
        Assert.Equal(box, view._status.Text);
        Assert.Equal("1 notice", view._noticeCount.Text);
    });

    [Fact]
    public void WithNoEngine_ThePanelSaysWhy() => RunSta(() =>
    {
        var plugin = new Plugin((_, _, _) => throw new InvalidOperationException("no device"));
        Start(plugin);
        using var panel = new Panel();

        plugin.OnDockablePanelCreated(panel);

        var label = Assert.IsType<Label>(Assert.Single(panel.Controls.Cast<Control>()));
        Assert.Contains("no device", label.Text);
    });

    /// <summary>Raises a control's protected mouse handler, as a click would.</summary>
    private static void Raise(Control control, string handler, MouseEventArgs e) =>
        typeof(Control).GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, new object[] { e });

    private static PluginView View(Panel panel) => Assert.IsType<PluginView>(Assert.Single(panel.Controls.Cast<Control>()));

    private static Color RowMarksSounding => RowMarks.Sounding;

    /// <summary>WinForms wants an STA thread; xunit does not give one.</summary>
    private static void RunSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
