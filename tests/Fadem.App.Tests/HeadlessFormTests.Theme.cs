using System.Reflection;
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

        // Every cell of the playing row: the seam buttons' cell draws its buttons in their own colours.
        Assert.All(rows[0].SubItems.Cast<ListViewItem.ListViewSubItem>(), cell => Assert.Equal(p.Accent, cell.ForeColor));
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

    /// <summary>How many pixels of <paramref name="colour"/> are around row
    /// <paramref name="index"/>'s seam, in its seam buttons' cell: a button showing there fills
    /// some in its own colour.</summary>
    private static int SeamPixels(Bitmap drawn, Fadem.App.MainForm form, int index, Color colour)
    {
        var cell = form._queueList.Items[index].SubItems[form.EditColumn].Bounds;
        var count = 0;
        for (var x = cell.Left; x < cell.Right; x++)
        {
            for (var y = cell.Bottom - 6; y < cell.Bottom + 6; y++)
            {
                count += drawn.GetPixel(x, y).ToArgb() == colour.ToArgb() ? 1 : 0;
            }
        }
        return count;
    }

    [Fact]
    public void TheOpenPair_IsTinted_AtEveryMatch_AndItsSeamFilledInTheAccent() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 5, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 5, 0.2);
        var c = TestAudio.WriteDcWav("gamma.wav", 5, 0.3);
        h.Form.AddPaths(new[] { a, b, c, a, b });
        h.Form.Render();
        var p = h.Form.Palette;
        var rows = h.Form._queueList.Items.Cast<ListViewItem>().ToArray();
        Assert.All(rows, row => Assert.Equal(p.Well, row.SubItems[1].BackColor));

        h.Form.EditTransitionAt(0);
        h.Form.Render();

        var tint = Palette.Mix(p.Accent, p.Well, 0.16);
        Assert.Equal(new[] { tint, tint, p.Well, tint, tint }, rows.Select(row => row.SubItems[1].BackColor));
        var drawn = QueuePixels(h.Form._queueList);
        Assert.True(SeamPixels(drawn, h.Form, 0, p.Accent) > 100); // filled, each match
        Assert.True(SeamPixels(drawn, h.Form, 3, p.Accent) > 100);
        Assert.Equal(0, SeamPixels(drawn, h.Form, 1, p.Accent)); // not the open pair's seam: none shown
    });

    [Fact]
    public void AnUnsavedSeam_ShowsItsButton_OnlyWhileTheMouseIsOnItsRow_RaisedAndWithTheHandOnTheButton() => RunSta(() =>
    {
        using var h = new Harness();
        h.Form.AddPaths(new[] { TestAudio.WriteDcWav("alpha.wav", 5, 0.4), TestAudio.WriteDcWav("beta.wav", 5, 0.2) });
        h.Form.Render();
        var p = h.Form.Palette;
        var list = h.Form._queueList;
        Assert.Equal(0, SeamPixels(QueuePixels(list), h.Form, 0, p.Raised)); // unsaved: no button

        var button = list.Items[0].SubItems[h.Form.EditColumn].Bounds;
        Move(list, new Point(button.X + button.Width / 2, button.Bottom - 3));

        Assert.Equal(Cursors.Hand, list.Cursor); // Equal: Cursors.Hand can race into two instances
        Assert.True(SeamPixels(QueuePixels(list), h.Form, 0, p.Raised) > 100); // shown, raised

        typeof(Control).GetMethod("OnMouseLeave", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(list, new object[] { EventArgs.Empty });

        Assert.Same(Cursors.Default, list.Cursor);
        Assert.Equal(0, SeamPixels(QueuePixels(list), h.Form, 0, p.Raised));
    });

    private static void Move(ListView list, Point at) =>
        typeof(Control).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(list, new object[] { new MouseEventArgs(MouseButtons.None, 0, at.X, at.Y, 0) });
}
