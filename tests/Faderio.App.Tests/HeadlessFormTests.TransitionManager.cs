using Faderio.App;
using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Tests;

namespace Faderio.App.Tests;

/// <summary>The transition manager (playlists issue 05, D24), on the headless main window.</summary>
public sealed partial class HeadlessFormTests
{
    private static TransitionSpec FadeOutAt(double seconds, double duration = 0.5) => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(seconds), FromEnd: false),
        FadeOutDuration = TimeSpan.FromSeconds(duration),
        FadeInPoint = TimeSpan.Zero,
        FadeInDuration = TimeSpan.FromSeconds(0.5),
    };

    /// <summary>Alpha and beta (20 s), gamma and delta (3 s) queued in Untitled; alpha → beta
    /// authored to fade out at 12 s and Untitled's override moving it to 9 s, alpha → gamma and
    /// delta → alpha authored too.</summary>
    private static (string A, string B, string C, string D) ManagerFixture(Harness h)
    {
        var a = TestAudio.WriteDcWav("alpha.wav", 20, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 20, 0.2);
        var c = TestAudio.WriteDcWav("gamma.wav", 3, 0.3);
        var d = TestAudio.WriteDcWav("delta.wav", 3, 0.1);
        h.Form.AddPaths(new[] { a, b, c, d });
        Track T(string path) => h.Engine.View.Queue.First(r => r.Track.FilePath == path).Track;
        h.Engine.Author(new Adjacency(T(a), T(b)), FadeOutAt(12));
        h.Engine.AuthorOverride(new Adjacency(T(a), T(b)), new TransitionSpec { FadeOutPoint = FadeOutAt(9).FadeOutPoint });
        h.Engine.Author(new Adjacency(T(a), T(c)), FadeOutAt(2));
        h.Engine.Author(new Adjacency(T(d), T(a)), FadeOutAt(2));
        return (a, b, c, d);
    }

    /// <summary>Presses Transitions… with the row at <paramref name="row"/> selected, or none.</summary>
    private static TransitionManagerForm OpenManager(Harness h, int? row)
    {
        TransitionManagerForm? shown = null;
        h.Form.ShowTransitionManager = form => shown = (TransitionManagerForm)form;
        h.Form._queueList.SelectedIndices.Clear();
        if (row is { } index)
        {
            SelectRows(h.Form._queueList, index);
        }
        Click(h.Form._openTransitions);
        return Assert.IsType<TransitionManagerForm>(shown);
    }

    private static string[] GridRow(TransitionManagerForm manager, int row) =>
        Enumerable.Range(0, 9).Select(c => (string)manager._grid.Rows[row].Cells[c].Value).ToArray();

    private static string Title(string path) => Path.GetFileNameWithoutExtension(path);

    [Fact]
    public void Transitions_OpensOnTheSelectedRowsTrack_AndListsEveryAdjacencyItIsIn() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b, c, d) = ManagerFixture(h);

        var manager = OpenManager(h, row: 0);

        Assert.Equal(a, manager.PickedTrack!.FilePath);
        Assert.Equal(4, manager._track.Items.Count);
        Assert.Equal(3, manager._grid.Rows.Count);
        Assert.Equal(["→", Title(b), "12 | 9", "0.5 | –", "0 | –", "0.5 | –", "",
            "fade-out at 9 s for 0.5 s, fade-in from 0 s over 0.5 s", ""], GridRow(manager, 0));
        Assert.Equal(["→", Title(c)], GridRow(manager, 1)[..2]);
        Assert.Equal("2 | –", GridRow(manager, 1)[2]);
        Assert.Equal(["←", Title(d)], GridRow(manager, 2)[..2]);
        Assert.Equal("Each value reads global | override in Untitled. Double-click a row to open it in the editor.", manager._legend.Text);
    });

    [Fact]
    public void Transitions_WithNoRowSelected_OpensOnTheStoresFirstTrack_AndThePickerChangesTheGrid() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b, _, _) = ManagerFixture(h);

        var manager = OpenManager(h, row: null);
        Assert.Equal(a, manager.PickedTrack!.FilePath); // alpha sorts first

        manager._track.SelectedIndex = 1; // beta

        Assert.Equal(b, manager.PickedTrack!.FilePath);
        Assert.Equal(["←", Title(a), "12 | 9"], GridRow(manager, 0)[..3]);
        Assert.Equal(1, manager._grid.Rows.Count);
    });

    [Fact]
    public void TheQueuesMenu_OpensTheManagerOnTheRowsTrack() => RunSta(() =>
    {
        using var h = new Harness();
        var (_, _, c, _) = ManagerFixture(h);
        TransitionManagerForm? shown = null;
        h.Form.ShowTransitionManager = form => shown = (TransitionManagerForm)form;
        SelectRows(h.Form._queueList, 2);

        h.Form._transitionsForTrack.PerformClick();

        Assert.Equal(c, shown!.PickedTrack!.FilePath);
        Assert.Equal("←", GridRow(shown, 0)[0]);
    });

    [Fact]
    public void Preview_PlaysTheOverridesFadeOutPoint_PreviewGlobal_TheGlobals_AndStopPreviewEndsIt() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b, _, _) = ManagerFixture(h);
        var manager = OpenManager(h, row: 0);
        Assert.False(manager._stopPreview.Enabled);

        manager.PreviewAt(0, TransitionLayer.Playlist);
        h.Output.Advance(44100);
        h.Form.Render();

        Assert.Equal(a, h.Engine.View.Preview!.Adjacency.Outgoing.FilePath);
        Assert.Equal(b, h.Engine.View.Preview.Adjacency.Incoming.FilePath);
        Assert.Equal(5.0, h.Engine.View.Preview.Outgoing!.Value.TotalSeconds, 3); // 9 s − 5 s, then 1 s
        Assert.Equal("Pre-roll · outgoing 0:05", manager._previewStatus.Text);
        Assert.True(manager._stopPreview.Enabled);

        manager.PreviewAt(0, TransitionLayer.Global);
        h.Output.Advance(44100);
        h.Form.Render();

        Assert.Equal(8.0, h.Engine.View.Preview!.Outgoing!.Value.TotalSeconds, 3); // 12 s − 5 s, then 1 s
        Assert.Equal("Pre-roll · outgoing 0:08", manager._previewStatus.Text);

        Click(manager._stopPreview);

        Assert.Null(h.Engine.View.Preview);
        Assert.Equal("", manager._previewStatus.Text);
        Assert.False(manager._stopPreview.Enabled);
    });

    [Fact]
    public void ADoubleClick_OpensTheRowInTheEditor_InTheModeItWasLeftIn() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b, _, _) = ManagerFixture(h);
        var editor = h.Form._editor;
        editor._editPlaylist.Checked = true;
        var manager = OpenManager(h, row: 0);

        manager.EditAt(0);

        Assert.Equal((a, b), (editor.Adjacency!.Outgoing.FilePath, editor.Adjacency.Incoming.FilePath));
        Assert.Equal(TransitionLayer.Playlist, editor.Layer);
        Assert.Equal("9", editor._fadeOutPoint.Text);
        Assert.Equal("12", editor._fadeOutPoint.PlaceholderText);
    });

    [Fact]
    public void ATransitionSavedInTheEditor_WhileTheManagerIsOpen_AppearsInIt() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, _, c, d) = ManagerFixture(h);
        var manager = OpenManager(h, row: 2); // gamma: only alpha → gamma
        Assert.Equal(1, manager._grid.Rows.Count);

        h.Form.EditTransitionAt(2); // gamma → delta
        h.Form._editor._fadeOutPoint.Text = "1";
        Click(h.Form._editor._save);

        Assert.Equal(c, manager.PickedTrack!.FilePath);
        Assert.Equal([("→", Title(d)), ("←", Title(a))],
            Enumerable.Range(0, manager._grid.Rows.Count).Select(r => (GridRow(manager, r)[0], GridRow(manager, r)[1])));
    });
}
