using System.Reflection;
using System.Windows.Forms;
using Faderio.Core.Playback;
using Faderio.Core.Tests;

namespace Faderio.MusicBee.Tests;

/// <summary>
/// Tracks dropped on the plugin's view (phase 6 issue 01): MusicBee's track drag is a shell drag
/// of the files, as Explorer's is, and the view sends them to the engine's Add, as the context
/// item sends the selection. Rows dragged in the view's queue move (issue 02), through the drag
/// the App's queue uses.
/// </summary>
public sealed partial class PluginTests
{
    /// <summary>What MusicBee's track drag allows, as probed on 3.6.9403.</summary>
    private const DragDropEffects MusicBeesDrag = DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link;

    [Fact]
    public void TracksDropped_OnTheQueue_AreAppendedInTheDragsOrder_WithoutPlaying() => RunSta(() =>
    {
        var (a, b) = AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        using var panel = new Panel();
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);
        var c = TestAudio.WriteToneWav("musicbee-c.wav", 2, 880);
        var d = TestAudio.WriteToneWav("musicbee-d.wav", 2, 220);

        var effect = Drop(view._queueList, new DataObject(DataFormats.FileDrop, new[] { d, c }));
        var queue = Added(4);

        Assert.Equal(DragDropEffects.Copy, effect);
        Assert.Equal(new[] { a, b, d, c }, queue.Queue.Select(r => r.Track.FilePath));
        Assert.Equal(new[] { a, b, d, c }, SavedOpenPlaylist(4));
        Assert.DoesNotContain("Player_Stop", _musicBee.Calls);
        Assert.Equal(Transport.Stopped, _output.State);
        Assert.Equal(0, _output.FramesRendered);
        view.Render();
        Assert.Equal(4, view._queueList.Items.Count);
        _ui.Pump();
        Assert.Empty(_plugin.Host!.Notices.Entries);
        Assert.Equal("Ready.", view._status.Text);
    });

    [Fact]
    public void RejectedFiles_InADrop_AreOneNotice_InTheViewsStrip() => RunSta(() =>
    {
        var (a, b) = AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        using var panel = new Panel();
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);
        var c = TestAudio.WriteToneWav("musicbee-c.wav", 2, 880);
        var text = Path.Combine(_dir, "notes.wav");
        File.WriteAllText(text, "not audio");
        var missing = Path.Combine(_dir, "missing.flac");

        Drop(view._queueList, new DataObject(DataFormats.FileDrop, new[] { c, text, missing }));
        var queue = Added(3);

        Assert.Equal(new[] { a, b, c }, queue.Queue.Select(r => r.Track.FilePath));
        Eventually(() => { _ui.Pump(); return _plugin.Host!.Notices.Entries.Count; }, n => n == 1, "No notice came.");
        var notice = _plugin.Host!.Notices.Latest!.Message;
        Assert.StartsWith("Added 1 track; rejected 2 — notes.wav: ", notice);
        Assert.Contains("; missing.flac: The file does not exist.", notice);
        Assert.Equal(notice, view._status.Text);
        Assert.Empty(_ui.Messages); // the strip, not a box
    });

    [Fact]
    public void DraggingSomethingOtherThanFiles_OnTheQueue_IsRefused() => RunSta(() =>
    {
        var (a, b) = AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        using var panel = new Panel();
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);

        var effect = Drop(view._queueList, new DataObject(DataFormats.UnicodeText, "not a path"));

        Assert.Equal(DragDropEffects.None, effect);
        var queue = _plugin.Host!.Engine.View;
        Assert.Null(queue.Adding);
        Assert.Equal(new[] { a, b }, queue.Queue.Select(r => r.Track.FilePath));
    });

    [Fact]
    public void RowsDragged_OntoALaterRowsUpperHalf_MoveThere_StaySelected_AndPlaybackGoesOn() => RunSta(() =>
    {
        var (a, b) = AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        using var panel = new Panel { Size = new System.Drawing.Size(900, 500) };
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);
        var c = TestAudio.WriteToneWav("musicbee-c.wav", 2, 880);
        var d = TestAudio.WriteToneWav("musicbee-d.wav", 2, 220);
        Drop(view._queueList, new DataObject(DataFormats.FileDrop, new[] { c, d }));
        Added(4);
        view.Render();
        Click(view._playPause);
        _output.Advance(4410);
        var list = view._queueList;
        _ = list.Handle;
        list.Items[3].Selected = true;

        var effect = DropAt(list, DragSelectedRows(view), UpperHalfOf(list, 1));

        Assert.Equal(DragDropEffects.Move, effect);
        Assert.Equal(new[] { a, d, b, c }, _plugin.Host!.Engine.View.Queue.Select(r => r.Track.FilePath));
        Assert.Equal(new[] { a, d, b, c }, SavedOpenPlaylist(4));
        view.Render();
        Assert.Equal(new[] { 1 }, list.SelectedIndices.Cast<int>());
        Assert.False(view._queueDrag.DropLine.Visible);
        Assert.Equal(Transport.Playing, _output.State);
        Assert.Equal(a, _plugin.Host.Engine.View.Queue.Single(r => r.IsCurrent).Track.FilePath);
    });

    [Fact]
    public void ADraggedRow_OverAnotherRowsUpperHalf_ShowsTheLineAboveIt() => RunSta(() =>
    {
        AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        using var panel = new Panel { Size = new System.Drawing.Size(900, 500) };
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);
        view.Render();
        var list = view._queueList;
        _ = list.Handle;
        list.Items[1].Selected = true;

        DropAt(list, DragSelectedRows(view), UpperHalfOf(list, 0), drop: false);

        Assert.True(IsSetVisible(view._queueDrag.DropLine));
        Assert.InRange(view._queueDrag.DropLine.Top, list.Items[0].Bounds.Top - 2, list.Items[0].Bounds.Top);
    });

    [Fact]
    public void RowsDraggedFromAnotherView_AreRefused() => RunSta(() =>
    {
        var (a, b) = AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        using var first = new Panel { Size = new System.Drawing.Size(900, 500) };
        using var second = new Panel { Size = new System.Drawing.Size(900, 500) };
        _plugin.OnDockablePanelCreated(first);
        _plugin.OnDockablePanelCreated(second);
        var (from, to) = (View(first), View(second));
        from.Render();
        to.Render();
        _ = from._queueList.Handle;
        _ = to._queueList.Handle;
        from._queueList.Items[1].Selected = true;

        var effect = DropAt(to._queueList, DragSelectedRows(from), UpperHalfOf(to._queueList, 0));

        Assert.Equal(DragDropEffects.None, effect);
        Assert.Equal(new[] { a, b }, _plugin.Host!.Engine.View.Queue.Select(r => r.Track.FilePath));
    });

    /// <summary>Delivers a drag to the list as Windows does for MusicBee's: DragEnter, then, if
    /// the list accepted it, DragDrop. Returns the effect the list offered. A list that never
    /// asked for drops gets neither, as Windows would not deliver them.</summary>
    private static DragDropEffects Drop(ListView list, IDataObject data)
    {
        if (!list.AllowDrop)
        {
            return DragDropEffects.None;
        }
        var args = new DragEventArgs(data, 0, 0, 0, MusicBeesDrag, DragDropEffects.None);
        typeof(Control).GetMethod("OnDragEnter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(list, new object[] { args });
        if (args.Effect != DragDropEffects.None)
        {
            typeof(Control).GetMethod("OnDragDrop", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(list, new object[] { args });
        }
        return args.Effect;
    }

    /// <summary>Starts a drag of the view's selected rows as a user would, but hands back what
    /// the list would drag instead of entering the modal drag loop.</summary>
    private static IDataObject DragSelectedRows(PluginView view)
    {
        IDataObject? dragged = null;
        view._queueDrag.StartDrag = data => dragged = data;
        typeof(ListView).GetMethod("OnItemDrag", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(view._queueList, new object[] { new ItemDragEventArgs(MouseButtons.Left, view._queueList.SelectedItems[0]) });
        return dragged ?? throw new InvalidOperationException("nothing was dragged");
    }

    /// <summary>Carries a drag over the list to a point in it, then drops it there. Returns the
    /// effect the list offered, and leaves the drop line as the drag over it left it.</summary>
    private static DragDropEffects DropAt(ListView list, IDataObject data, System.Drawing.Point client, bool drop = true)
    {
        var screen = list.PointToScreen(client);
        var args = new DragEventArgs(data, 0, screen.X, screen.Y, DragDropEffects.Move, DragDropEffects.None);
        typeof(Control).GetMethod("OnDragEnter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(list, new object[] { args });
        typeof(Control).GetMethod("OnDragOver", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(list, new object[] { args });
        if (drop && args.Effect != DragDropEffects.None)
        {
            typeof(Control).GetMethod("OnDragDrop", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(list, new object[] { args });
        }
        return args.Effect;
    }

    private static System.Drawing.Point UpperHalfOf(ListView list, int index) =>
        new(list.Items[index].Bounds.Left + 8, list.Items[index].Bounds.Top + 2);

    /// <summary>Whether the control itself is set visible. <see cref="Control.Visible"/> also
    /// asks every parent, and a panel that is never shown has none visible.</summary>
    private static bool IsSetVisible(Control control)
    {
        var getState = typeof(Control).GetMethod("GetState", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var type = getState.GetParameters()[0].ParameterType; // an int on net48, an enum after
        var visible = type.IsEnum ? Enum.ToObject(type, 0x2) : 0x2; // States.Visible
        return (bool)getState.Invoke(control, new[] { visible })!;
    }
}
