using Fadem.Core.Tests;
using Fadem.Shell.Theme;

namespace Fadem.App.Tests;

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

    /// <summary>Round 2 issue 12: Cue transition is ⇥, "go to a point", drawn in Segoe UI Symbol
    /// (which has it) at ⤳'s 13 pt, as large as ⏭ near enough, dimmer when greyed, and centred
    /// as ⏭ is.</summary>
    [Fact]
    public void CueTransition_IsAnArrowToABar_DrawnAndCentredAsTheNextKeyIs() => RunSta(() =>
    {
        using var h = new Harness();
        Assert.Equal("⇥", h.Form._cueTransition.Text);

        var greyed = KeyInk(h.Form._cueTransition); // stopped: greyed
        var next = KeyInk(h.Form._next);
        h.Form._cueTransition.Enabled = true;
        var lit = KeyInk(h.Form._cueTransition);

        Assert.False(greyed.Bounds.IsEmpty, "no glyph drawn while greyed");
        Assert.True(greyed.Strength < lit.Strength, $"greyed stands off the face by {greyed.Strength}, live by {lit.Strength}");
        Assert.True(lit.Bounds.Width >= 10 && lit.Bounds.Height >= 6, $"the glyph is only {lit.Bounds.Size}, ⏭ {next.Bounds.Size}");
        PointF Centre(Rectangle r, Control key) => new(r.Left + r.Width / 2f - key.Width / 2f, r.Top + r.Height / 2f - key.Height / 2f);
        var (cue, beside) = (Centre(lit.Bounds, h.Form._cueTransition), Centre(next.Bounds, h.Form._next));
        Assert.True(Math.Abs(cue.X - beside.X) <= 2 && Math.Abs(cue.Y - beside.Y) <= 2, $"⇥ is off by {cue}, ⏭ by {beside}");
    });

    /// <summary>A key's glyph as drawn: the bounds of the pixels inside its edge that stand off
    /// its face (sampled at its left, middle) by more than a faint fringe, and how far the most
    /// distinct of them stands off it.</summary>
    private static (Rectangle Bounds, int Strength) KeyInk(Control key)
    {
        using var bitmap = new Bitmap(key.Width, key.Height);
        key.DrawToBitmap(bitmap, new Rectangle(Point.Empty, key.Size));
        var face = bitmap.GetPixel(4, key.Height / 2);
        var ink = (from x in Enumerable.Range(4, key.Width - 8)
                   from y in Enumerable.Range(4, key.Height - 8)
                   let c = bitmap.GetPixel(x, y)
                   let off = Math.Abs(c.R - face.R) + Math.Abs(c.G - face.G) + Math.Abs(c.B - face.B)
                   where off > 90
                   select (At: new Point(x, y), Off: off)).ToList();
        return ink.Count == 0 ? (Rectangle.Empty, 0)
            : (Rectangle.FromLTRB(ink.Min(q => q.At.X), ink.Min(q => q.At.Y), ink.Max(q => q.At.X) + 1, ink.Max(q => q.At.Y) + 1), ink.Max(q => q.Off));
    }

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
        // The accent is Windows' default selection blue (round 2 issue 07), so a pixel of it is no stock one.
        var stock = new[] { SystemColors.Window, SystemColors.Info, Fadem.Shell.RowMarks.Selected, SystemColors.Highlight }.Select(c => c.ToArgb())
            .Where(c => c != p.Accent.ToArgb()).ToHashSet();
        // Within the rows, which the style draws; the frame and scroll bars are the theme's.
        var drawnRows = rows.Cast<ListViewItem>().Select(i => Rectangle.Intersect(i.Bounds, new Rectangle(System.Drawing.Point.Empty, drawn.Size)));
        Assert.DoesNotContain(drawnRows.SelectMany(r => Enumerable.Range(r.Left, r.Width)
                .SelectMany(x => Enumerable.Range(r.Top, r.Height).Select(y => drawn.GetPixel(x, y).ToArgb()))),
            stock.Contains);
        drawn.Dispose();
    });

    [Fact]
    public void TheOpenPlaylist_IsInTheAccent_OnRaised_AndTheHeadingIsInText() => RunSta(() =>
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
        Assert.Equal(p.Text, heading.ForeColor); // round 2 issue 06
        Assert.True(heading.Font.Bold);
    });

    // ── Studio theme issue 04: the seek bar and the LED readout ─────────────────────────

    [Fact]
    public void TheReadout_IsTheLedFaceInText_WholeAtFullSize_AndTheSeekBarTakesThePalette() => RunSta(() =>
    {
        using var h = new Harness();
        h.Form.AddPaths(new[] { TestAudio.WriteDcWav("alpha.wav", 5, 0.4) });
        h.Form.PlayRowAt(0);
        h.Form.Render();
        var p = h.Form.Palette;

        Assert.Same(p, h.Form._seek.Palette);
        Assert.Equal(p.Text, h.Form._time.ForeColor); // round 2 issue 06
        Assert.Equal(p.Dim, h.Form._seconds.ForeColor);
        var font = h.Form._time.DrawnFont;
        Assert.Equal(("Consolas", Fadem.Shell.Readout.LedSize), (font.Name, font.Size));
        var size = TextRenderer.MeasureText(h.Form._time.Text, font, Size.Empty, TextFormatFlags.NoPadding);
        Assert.True(size.Width <= h.Form._time.Width && size.Height <= h.Form._time.Height, $"the readout, {size}, does not fit {h.Form._time.Size}");
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
