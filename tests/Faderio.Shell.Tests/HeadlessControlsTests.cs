using System.Runtime.ExceptionServices;
using Faderio.Core.Domain;
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
    public void TheSeekBar_ShowsAPlayhead_AndMapsAClickBackToOne() => RunSta(() =>
    {
        using var bar = new SeekBar { Width = 400, Height = 20 };

        bar.Show(Playhead.FromMilliseconds(30_000), new Length(TimeSpan.FromSeconds(60)));

        Assert.True(bar.Enabled);
        Assert.Equal(30_000, bar.Playhead.Milliseconds);
        Assert.Equal(30_000, bar.PlayheadAt(SeekBar.Inset + (400 - 2 * SeekBar.Inset) / 2).Milliseconds);
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
