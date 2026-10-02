using System.Reflection;
using System.Runtime.ExceptionServices;
using Faderio.Core.Tests;
using Faderio.Shell;

namespace Faderio.Shell.Tests;

/// <summary>The queue's drag, shared by both hosts (phase 6 issue 02), on each host's runtime:
/// where a drop lands, and that a drop of rows is one Move and of files the host's add.</summary>
public sealed class QueueDragTests
{
    [Fact]
    public void ADrop_LandsBeforeTheRowsUpperHalf_AfterItsLowerHalf_AndAtTheEndBelowTheRows() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        using var list = QueueOf(tc, 3);
        using var drag = new QueueDrag(list, tc.Engine, _ => { });
        var row = list.Items[1].Bounds;

        Assert.Equal(1, drag.DropIndex(new Point(8, row.Top + 2)));
        Assert.Equal(2, drag.DropIndex(new Point(8, row.Bottom - 2)));
        Assert.Equal(3, drag.DropIndex(new Point(8, list.Items[2].Bounds.Bottom + 20)));
        Assert.Equal(1, drag.DropIndex(new Point(list.ClientSize.Width - 4, row.Top + 2))); // past the columns
    });

    [Fact]
    public void RowsDropped_AreOneMove_BeforeTheRowAtTheDrop() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        using var list = QueueOf(tc, 3);
        using var drag = new QueueDrag(list, tc.Engine, _ => { });
        var paths = tc.View.Queue.Select(r => r.Track.FilePath).ToArray();
        IDataObject? dragged = null;
        drag.StartDrag = data => dragged = data;
        list.Items[2].Selected = true;
        typeof(ListView).GetMethod("OnItemDrag", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(list, new object[] { new ItemDragEventArgs(MouseButtons.Left, list.Items[2]) });

        var effect = DropAt(list, dragged!, new Point(8, list.Items[0].Bounds.Top + 2));

        Assert.Equal(DragDropEffects.Move, effect);
        Assert.Equal(new[] { paths[2], paths[0], paths[1] }, tc.View.Queue.Select(r => r.Track.FilePath));
    });

    [Fact]
    public void FilesDropped_GoToTheHost_InTheOrderDropped_AndTextIsRefused() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        using var list = QueueOf(tc, 1);
        var dropped = new List<string[]>();
        using var drag = new QueueDrag(list, tc.Engine, dropped.Add);
        var point = new Point(8, list.Items[0].Bounds.Bottom - 2);

        var files = DropAt(list, new DataObject(DataFormats.FileDrop, new[] { "b.wav", "a.wav" }), point);
        var text = DropAt(list, new DataObject(DataFormats.UnicodeText, "not a path"), point);

        Assert.Equal(DragDropEffects.Copy, files);
        Assert.Equal(DragDropEffects.None, text);
        Assert.Equal(new[] { "b.wav", "a.wav" }, Assert.Single(dropped));
    });

    /// <summary>A details list of the engine's queue, <paramref name="rows"/> tones long, each
    /// item carrying its row's id as both hosts' queues do, laid out but never shown.</summary>
    private static ListView QueueOf(TestEngine tc, int rows)
    {
        tc.Engine.Add(Enumerable.Range(0, rows).Select(i => TestAudio.WriteDcWav($"drag-{i}.wav", 1, 0.2))).Wait();
        var list = new ListView { View = View.Details, FullRowSelect = true, MultiSelect = true, Size = new Size(400, 300) };
        list.Columns.Add("Title", 120);
        foreach (var row in tc.View.Queue)
        {
            list.Items.Add(new ListViewItem(Path.GetFileName(row.Track.FilePath)) { Tag = row.Id });
        }
        _ = list.Handle;
        return list;
    }

    private static DragDropEffects DropAt(ListView list, IDataObject data, Point client)
    {
        var screen = list.PointToScreen(client);
        var args = new DragEventArgs(data, 0, screen.X, screen.Y, DragDropEffects.Copy | DragDropEffects.Move, DragDropEffects.None);
        typeof(Control).GetMethod("OnDragEnter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(list, new object[] { args });
        typeof(Control).GetMethod("OnDragOver", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(list, new object[] { args });
        if (args.Effect != DragDropEffects.None)
        {
            typeof(Control).GetMethod("OnDragDrop", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(list, new object[] { args });
        }
        return args.Effect;
    }

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
