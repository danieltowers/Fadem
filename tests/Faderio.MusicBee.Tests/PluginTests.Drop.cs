using System.Reflection;
using System.Windows.Forms;
using Faderio.Core.Playback;
using Faderio.Core.Tests;

namespace Faderio.MusicBee.Tests;

/// <summary>
/// Tracks dropped on the plugin's view (phase 6 issue 01): MusicBee's track drag is a shell drag
/// of the files, as Explorer's is, and the view sends them to the engine's Add, as the context
/// item sends the selection.
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
}
