using Faderio.Core.Tests;
using Faderio.Shell.Theme;

namespace Faderio.App.Tests;

/// <summary>Studio theme issue 02: the main window in the studio look.</summary>
public sealed partial class HeadlessFormTests
{
    private static IEnumerable<Control> AllControls(Control root) =>
        new[] { root }.Concat(root.Controls.Cast<Control>().SelectMany(AllControls));

    [Fact]
    public void TheMainWindow_HasNoSystemColourLeft_AndEveryButtonIsAKey() => RunSta(() =>
    {
        using var h = new Harness();
        h.Form.AddPaths(new[] { TestAudio.WriteDcWav("alpha.wav", 3, 0.4), TestAudio.WriteDcWav("beta.wav", 2, 0.2) });
        h.Form.Render();

        var controls = AllControls(h.Form).ToList();

        Assert.Empty(controls.Where(c => c.BackColor.IsSystemColor || c.ForeColor.IsSystemColor).Select(c => $"{c.GetType().Name} '{c.Text}'"));
        Assert.All(controls.OfType<Button>(), b => Assert.Equal(FlatStyle.Flat, b.FlatStyle));
        Assert.Equal(h.Form.Palette.Chrome, h.Form.BackColor);
        Assert.Equal(h.Form.Palette.Well, h.Form._queueList.BackColor);
        Assert.True(h.Form.Palette.Same(Palette.Studio)); // no palette.json: the built-in
    });

    // ── Studio theme issue 03: the queue, the playlists and their labels ────────────────

    [Fact]
    public void ThePlayingRow_IsInTheAccent_AndASelectedRow_IsDrawnRaised() => RunSta(() =>
    {
        using var h = new Harness();
        h.Form.AddPaths(new[] { TestAudio.WriteDcWav("alpha.wav", 5, 0.4), TestAudio.WriteDcWav("beta.wav", 5, 0.2) });
        h.Form.PlayRowAt(0);
        h.Form.Render();
        var p = h.Form.Palette;
        var rows = h.Form._queueList.Items;

        // Every cell of the playing row but the ✎, which is dim while not authored.
        Assert.All(rows[0].SubItems.Cast<ListViewItem.ListViewSubItem>().Where((_, i) => i != h.Form.EditColumn),
            cell => Assert.Equal(p.Accent, cell.ForeColor));
        Assert.Equal(p.Text, rows[1].ForeColor);

        rows[1].Selected = true;
        var drawn = QueuePixels(h.Form._queueList);
        var row = rows[1].Bounds;
        // The row's ground, its commonest colour: any one pixel may be a glyph's.
        var ground = Enumerable.Range(row.Left, Math.Min(row.Width, drawn.Width - row.Left))
            .Select(x => drawn.GetPixel(x, row.Top + row.Height / 2).ToArgb())
            .GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key;
        Assert.Equal(p.Raised.ToArgb(), ground);
        var stock = new[] { SystemColors.Window, SystemColors.Info, Faderio.Shell.RowMarks.Selected, SystemColors.Highlight }.Select(c => c.ToArgb()).ToHashSet();
        // Within the rows, which the style draws; the frame and scroll bars are the theme's.
        var drawnRows = rows.Cast<ListViewItem>().Select(i => Rectangle.Intersect(i.Bounds, new Rectangle(System.Drawing.Point.Empty, drawn.Size)));
        Assert.DoesNotContain(drawnRows.SelectMany(r => Enumerable.Range(r.Left, r.Width)
                .SelectMany(x => Enumerable.Range(r.Top, r.Height).Select(y => drawn.GetPixel(x, y).ToArgb()))),
            stock.Contains);
        drawn.Dispose();
    });

    [Fact]
    public void TheOpenPlaylist_IsInTheAccent_OnRaised_AndTheHeadingIsLit() => RunSta(() =>
    {
        using var h = new Harness();
        h.Form.Render();
        var p = h.Form.Palette;
        var sidebar = h.Form._playlists;

        var open = sidebar._list.Items.Cast<ListViewItem>().Single(i => i.Font.Bold);
        Assert.Equal(p.Accent, open.ForeColor);
        Assert.Equal(p.Raised, open.BackColor);
        var heading = sidebar.Controls.OfType<Label>().Single();
        Assert.Equal("Playlists", heading.Text);
        Assert.Equal(p.Accent, heading.ForeColor);
        Assert.True(heading.Font.Bold);
    });

    /// <summary>The queue as drawn: through its own owner draw, from a print of its window.</summary>
    private static Bitmap QueuePixels(ListView list)
    {
        list.FindForm()!.CreateControl();
        _ = list.Handle;
        var bitmap = new Bitmap(list.Width, list.Height);
        list.DrawToBitmap(bitmap, new Rectangle(0, 0, list.Width, list.Height));
        return bitmap;
    }

    [Fact]
    public void PlayPause_LightsItsLed_WhilePlaying() => RunSta(() =>
    {
        using var h = new Harness();
        h.Form.AddPaths(new[] { TestAudio.WriteDcWav("alpha.wav", 5, 0.4) });
        h.Form.Render();
        Assert.False(StudioTheme.IsLit(h.Form._playPause));

        h.Form.PlayRowAt(0);
        h.Form.Render();
        Assert.True(StudioTheme.IsLit(h.Form._playPause));

        Click(h.Form._playPause); // pause
        h.Form.Render();
        Assert.False(StudioTheme.IsLit(h.Form._playPause));
    });
}
