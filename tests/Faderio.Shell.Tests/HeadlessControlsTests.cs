using System.Runtime.ExceptionServices;
using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Tests;
using Faderio.Shell;

namespace Faderio.Shell.Tests;

/// <summary>The shared controls constructed on each host's runtime, never shown, against a real
/// engine on the fake output (phase 5 issue 09): what the plugin will compose has to come up on
/// net48 x86 as it does on net8.0-windows, placeholders included, which net48's text box has
/// no property for.</summary>
public sealed class HeadlessControlsTests
{
    [Fact]
    public void TheSidebar_ListsTheLibrarysPlaylists() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        using var sidebar = new PlaylistSidebar(tc.Engine, () => true);

        sidebar.Render(tc.View);

        var names = sidebar._list.Items.Cast<ListViewItem>().Select(i => i.Text).ToList();
        Assert.Equal(tc.View.Playlists, names);
        Assert.Contains(tc.View.OpenPlaylist, names);
    });

    [Fact]
    public void TheEditor_OpensAnAdjacency_WithTheBlankDurationAsAPlaceholder() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var a = tc.TrackOfLength(Path.Combine(tc.TempDir, "a.wav"), 30);
        var b = tc.TrackOfLength(Path.Combine(tc.TempDir, "b.wav"), 30);
        tc.Engine.AddTracks(new[] { a, b });
        using var editor = new TransitionEditorPanel(tc.Engine, _ => { }, _ => null, _ => UnsavedEdits.Discard);

        editor.Open(new Adjacency(a, b));
        editor.Refresh(tc.View);

        Assert.Equal(new Adjacency(a, b), editor.Adjacency);
        Assert.Equal("", editor._fadeOutDuration.Text);
        Assert.Equal("5", Placeholders.Get(editor._fadeOutDuration)); // the library's blank duration
        Assert.Equal("5", Placeholders.Get(editor._fadeInDuration));
    });

    [Fact]
    public void TheManager_ComesUpWithItsGrid() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        using var manager = new TransitionManagerForm(tc.Engine, _ => { });

        Assert.Equal(TransitionManagerForm.PreviewGlobalColumn + 1, manager._grid.Columns.Count);
        Assert.False(manager._stopPreview.Enabled);
    });

    [Fact]
    public void TheManager_RaisesBeforeSounding_BeforeAStoredPreviewSounds_AndNotForStopPreview() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        tc.Engine.Add(new[] { TestAudio.WriteDcWav("manager-a.wav", 10, 0.3), TestAudio.WriteDcWav("manager-b.wav", 10, 0.3) }).Wait();
        var (a, b) = (tc.View.Queue[0].Track, tc.View.Queue[1].Track);
        tc.Engine.Author(new Adjacency(a, b), new TransitionSpec { FadeOutPoint = TimeSpan.FromSeconds(6) });
        var outputAtHook = new List<(Transport, int)>();
        using var manager = new TransitionManagerForm(tc.Engine, _ => { }, beforeSounding: () => outputAtHook.Add((tc.Output.State, tc.Output.ReadCallCount)));
        manager.Open(a);

        manager.PreviewAt(0, TransitionLayer.Global);
        tc.Output.Advance(441);

        Assert.Equal(new[] { (Transport.Stopped, 0) }, outputAtHook);
        Assert.NotNull(tc.View.Preview);
        manager.Render(tc.View);
        Assert.True(manager._stopPreview.Enabled);

        Click(manager._stopPreview);

        Assert.Null(tc.View.Preview);
        Assert.Single(outputAtHook);
    });

    /// <summary>Raises a button's Click as a press would, where an unshown button's own
    /// <c>PerformClick</c> does nothing.</summary>
    private static void Click(ButtonBase button)
    {
        Assert.True(button.Enabled, $"{button.Text} is disabled");
        typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(button, new object[] { EventArgs.Empty });
    }

    [Fact]
    public void TheSeekBar_ShowsAPlayhead_AndMapsAClickBackToOne() => RunSta(() =>
    {
        using var bar = new SeekBar { Width = 400, Height = 20 };

        bar.Show(Playhead.FromMilliseconds(30_000), new Length(TimeSpan.FromSeconds(60)));

        Assert.True(bar.Enabled);
        Assert.Equal(30_000, bar.Playhead.Milliseconds);
        Assert.Equal(30_000, bar.PlayheadAt(SeekBar.Inset + (400 - 2 * SeekBar.Inset) / 2).Milliseconds);
    });

    [Fact]
    public void TheRowStyle_MarksTheAttendedRow_AndSetsThePencil_BoldWhileAuthored_BlankOnTheLastRow() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var a = tc.TrackOfLength(Path.Combine(tc.TempDir, "a.wav"), 30);
        var b = tc.TrackOfLength(Path.Combine(tc.TempDir, "b.wav"), 30);
        tc.Engine.AddTracks(new[] { a, b });
        tc.Engine.Author(new Adjacency(a, b), new TransitionSpec { FadeOutPoint = TimeSpan.FromSeconds(20) });
        var view = tc.View;
        using var style = new QueueRowStyle();
        using var list = new ListView { View = View.Details };
        list.Columns.Add("#"); list.Columns.Add("Title"); list.Columns.Add("");
        var rows = view.Queue.Select(r => new ListViewItem(new[] { "", r.Track.Title, "" }) { UseItemStyleForSubItems = false }).ToList();
        list.Items.AddRange(rows.ToArray());

        for (var i = 0; i < rows.Count; i++)
        {
            style.Apply(rows[i], view.Queue[i], i, view, editColumn: 2);
        }

        Assert.Equal("1", rows[0].Text);
        Assert.Equal(RowMarks.Resting, rows[0].SubItems[1].BackColor); // the attended row, at rest
        Assert.Equal(SystemColors.Window, rows[1].SubItems[1].BackColor);
        Assert.Equal("✎", rows[0].SubItems[2].Text);
        Assert.True(rows[0].SubItems[2].Font.Bold);
        Assert.Equal(SystemColors.WindowText, rows[0].SubItems[2].ForeColor);
        Assert.Equal("", rows[1].SubItems[2].Text); // the last row has no next

        tc.Engine.Unauthor(new Adjacency(a, b));
        style.Apply(rows[0], tc.View.Queue[0], 0, tc.View, editColumn: 2);

        Assert.Equal("✎", rows[0].SubItems[2].Text);
        Assert.False(rows[0].SubItems[2].Font.Bold);
        Assert.Equal(SystemColors.GrayText, rows[0].SubItems[2].ForeColor);
    });

    [Fact]
    public void TheEditorsButtonRows_WrapInASidebar_AndNotAtTheAppsWidth() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var a = tc.TrackOfLength(Path.Combine(tc.TempDir, "a.wav"), 30);
        var b = tc.TrackOfLength(Path.Combine(tc.TempDir, "b.wav"), 30);
        tc.Engine.AddTracks(new[] { a, b });
        using var editor = new TransitionEditorPanel(tc.Engine, _ => { }, _ => null, _ => UnsavedEdits.Discard) { Height = 800 };
        editor.Open(new Adjacency(a, b));

        editor.Width = 340; // standalone faderio's
        editor.PerformLayout();
        Assert.Equal(editor._save.Top, editor._remove.Top); // one row

        editor.Width = 265; // a sidebar's, less a scroll bar
        editor.PerformLayout();

        Assert.True(editor._remove.Top > editor._save.Top, "Remove transition did not wrap below Save");
        Assert.True(editor._remove.Right <= editor._remove.Parent!.ClientSize.Width, "Remove transition is off the edge");
        Assert.True(editor._save.Parent!.Height >= 64, $"the wrapped row is only {editor._save.Parent!.Height} px tall");
    });

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
