using System.Drawing;
using System.Windows.Forms;
using Faderio.Shell.Theme;

namespace Faderio.MusicBee.Tests;

/// <summary>Studio theme issue 01: the plugin's view takes its palette from MusicBee's skin, and
/// writes it where the App on this machine finds it.</summary>
public sealed partial class PluginTests
{
    private static int Opaque(int rgb) => unchecked((int)0xFF000000) | rgb;

    /// <summary>What the user's Duophonic skin answered a probe on 2026-10-06.</summary>
    private void AnswerDuophonic()
    {
        _musicBee.Skin[(0, 0, 1)] = Opaque(0x232323);
        _musicBee.Skin[(0, 0, 3)] = Opaque(0xE1E1E1);
        _musicBee.Skin[(2, 0, 1)] = Opaque(0x1C1C1C);
        _musicBee.Skin[(16, 0, 0)] = Opaque(0x121212);
        _musicBee.Skin[(7, 0, 1)] = Opaque(0x2C2C2C);
        _musicBee.Skin[(7, 3, 3)] = Opaque(0x6D6D6D);
        _musicBee.Skin[(7, 4, 3)] = Opaque(0xCD5C5C); // its red accent, which faderio does not take
    }

    private PluginView PaletteView(Panel panel)
    {
        AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        _plugin.OnDockablePanelCreated(panel);
        return View(panel);
    }

    [Fact]
    public void TheView_TakesItsPaletteFromMusicBeesSkin_AndWritesItForTheApp() => RunSta(() =>
    {
        AnswerDuophonic();
        var file = Path.Combine(_dir, "app machine", Palette.FileName);
        _plugin.PaletteFile = file;
        using var panel = new Panel { Size = new Size(900, 500) };

        var view = PaletteView(panel);

        Assert.Equal("#1C1C1C", Palette.Hex(view.Palette.Chrome)); // MusicBee's own bars
        Assert.Equal("#121212", Palette.Hex(view.Palette.Well));
        Assert.Equal("#0078D7", Palette.Hex(view.Palette.Accent)); // round 2 issue 07: the blue, not the skin's red
        Assert.True(Palette.Load(file)!.Same(view.Palette));
    });

    /// <summary>Round 2 issue 01: a one-pixel rule in the palette's Edge across the view's top, so
    /// MusicBee's strip and faderio do not run together; the controls start under it.</summary>
    [Fact]
    public void TheViewsTop_IsARuleInEdge_TheFullWidth() => RunSta(() =>
    {
        AnswerDuophonic();
        using var panel = new Panel { Size = new Size(900, 500) };
        var view = PaletteView(panel);

        using var bitmap = new Bitmap(view.Width, view.Height);
        view.DrawToBitmap(bitmap, new Rectangle(Point.Empty, view.Size));

        var edge = view.Palette.Edge.ToArgb();
        Assert.All(Enumerable.Range(0, view.Width), x => Assert.Equal(edge, bitmap.GetPixel(x, 0).ToArgb()));
        Assert.Equal(1, view.Controls.Cast<Control>().Where(c => c.Visible).Min(c => c.Top));
    });

    /// <summary>Round 2 issue 05: the logo at the right of the track's line, white on a dark skin
    /// and black on a light one, and in a sidebar the track's name is cut short before it.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheLogo_IsAtTheTrackLinesRight_InTheArtForTheSkin(bool light) => RunSta(() =>
    {
        if (light)
        {
            _musicBee.Skin[(0, 0, 1)] = Opaque(0xF0F0F0);
            _musicBee.Skin[(2, 0, 1)] = Opaque(0xFFFFFF);
            _musicBee.Skin[(0, 0, 3)] = Opaque(0x202020);
        }
        else
        {
            AnswerDuophonic();
        }
        using var panel = new Panel { Size = new Size(284, 500) }; // MusicBee's sidebar as shipped
        var view = PaletteView(panel);

        Assert.Equal(!light, view.Palette.IsDark);
        Assert.Equal(!light, view._logo.IsDark);
        var line = view._logo.Parent!;
        Assert.Same(line, view._attendedTrack.Parent);
        Assert.Equal(line.ClientSize.Width, view._logo.Right);
        Assert.Equal(view._logo.Left, view._attendedTrack.Right);
        Assert.True(view._attendedTrack.Width > line.ClientSize.Width / 2); // the name keeps most of the line
    });

    /// <summary>Round 2 issue 05, at three times the line (2026-10-10): in a main panel the logo is
    /// a column of its own beside the track's line and the buttons, at its full height.</summary>
    [Fact]
    public void InAMainPanel_TheLogoIsAColumnBesideTheTracksLineAndTheButtons_AtFullHeight() => RunSta(() =>
    {
        AnswerDuophonic();
        using var panel = new Panel { Size = new Size(1200, 700) };
        var view = PaletteView(panel);

        Assert.NotSame(view._logo.Parent, view._attendedTrack.Parent);
        Assert.Equal(view.ClientSize.Width, view._logo.Right);
        Assert.Equal(view._logo.Room - 4, view._logo.ArtSize.Height);
        Assert.True(view._logo.Parent!.Height >= view._logo.Room);
        Assert.True(view._attendedTrack.Parent!.Parent!.Right <= view._logo.Left);
        Assert.True(view._playPause.Bottom + view._playPause.Parent!.Top <= view._logo.Bottom); // the buttons beside it
    });

    /// <summary>The user saw the logo cut at its left (2026-10-10): the view starts narrow, so the
    /// logo starts on the track's line at that line's width, and widening put it in its column
    /// without the width for its full height until the next resize.</summary>
    [Fact]
    public void APanelThatWidens_GivesTheLogoTheWidthOfItsFullHeight() => RunSta(() =>
    {
        AnswerDuophonic();
        using var panel = new Panel { Size = new Size(284, 700) };
        var view = PaletteView(panel);
        Assert.Same(view._attendedTrack.Parent, view._logo.Parent);

        panel.Width = 1200;

        Assert.NotSame(view._attendedTrack.Parent, view._logo.Parent);
        Assert.Equal(view._logo.Room - 4, view._logo.ArtSize.Height);
        Assert.Equal(view._logo.ArtSize.Width + 4, view._logo.Width);
        Assert.Equal(view.ClientSize.Width, view._logo.Right);
    });

    /// <summary>Round 2 issue 12: Cue transition is ⇥ on a dark and a light skin, drawn at
    /// ⤳'s 13 pt, dimmer when greyed, and centred as ⏭ is.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CueTransition_IsAnArrowToABar_DrawnAndCentredAsTheNextKeyIs(bool light) => RunSta(() =>
    {
        if (light)
        {
            _musicBee.Skin[(0, 0, 1)] = Opaque(0xF0F0F0);
            _musicBee.Skin[(2, 0, 1)] = Opaque(0xFFFFFF);
            _musicBee.Skin[(0, 0, 3)] = Opaque(0x202020);
        }
        else
        {
            AnswerDuophonic();
        }
        using var panel = new Panel { Size = new Size(900, 500) };
        var view = PaletteView(panel);
        Assert.Equal(!light, view.Palette.IsDark);
        Assert.Equal("⇥", view._cueTransition.Text);

        var greyed = KeyInk(view._cueTransition); // stopped: greyed
        var next = KeyInk(view._next);
        view._cueTransition.Enabled = true;
        var lit = KeyInk(view._cueTransition);

        Assert.False(greyed.Bounds.IsEmpty, "no glyph drawn while greyed");
        Assert.True(greyed.Strength < lit.Strength, $"greyed stands off the face by {greyed.Strength}, live by {lit.Strength}");
        Assert.True(lit.Bounds.Width >= 10 && lit.Bounds.Height >= 6, $"the glyph is only {lit.Bounds.Size}, ⏭ {next.Bounds.Size}");
        PointF Centre(Rectangle r, Control key) => new(r.Left + r.Width / 2f - key.Width / 2f, r.Top + r.Height / 2f - key.Height / 2f);
        var (cue, beside) = (Centre(lit.Bounds, view._cueTransition), Centre(next.Bounds, view._next));
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

    [Fact]
    public void ASkinThatAnswersNothing_GivesTheBuiltInPalette() => RunSta(() =>
    {
        using var panel = new Panel { Size = new Size(900, 500) };

        var view = PaletteView(panel);

        Assert.True(view.Palette.Same(Palette.Studio));
    });

    [Fact]
    public void TheView_IsInTheSkinsColours_WithNoSystemColourLeft() => RunSta(() =>
    {
        AnswerDuophonic();
        using var panel = new Panel { Size = new Size(900, 500) };

        var view = PaletteView(panel);
        view.Render();
        IEnumerable<Control> All(Control c) => new[] { c }.Concat(c.Controls.Cast<Control>().SelectMany(All));

        Assert.Empty(All(view).Where(c => c.BackColor.IsSystemColor || c.ForeColor.IsSystemColor).Select(c => $"{c.GetType().Name} '{c.Text}'"));
        Assert.Equal("#1C1C1C", Palette.Hex(view.BackColor)); // MusicBee's own bars: no seam
        Assert.Equal("#121212", Palette.Hex(view._queueList.BackColor));
        Assert.All(All(view).OfType<Button>(), b => Assert.Equal(FlatStyle.Flat, b.FlatStyle));

        Click(view._playPause);
        view.Render();
        Assert.True(StudioTheme.IsLit(view._playPause));
    });

    [Fact]
    public void InTheSidebar_TheLedReadoutIsWholeAndInText_AndTheSeekRowHoldsOneLine() => RunSta(() =>
    {
        AnswerDuophonic();
        using var panel = new Panel { Size = new Size(285, 700) }; // MusicBee's sidebar, as shipped
        var view = PaletteView(panel);
        Click(view._playPause);
        view.Render();

        Assert.Equal(view.Palette.Text, view._time.ForeColor); // round 2 issue 06
        Assert.Equal(view.Palette.Dim, view._seconds.ForeColor);
        Assert.StartsWith("0:00 / -", view._time.Text);
        var font = view._time.DrawnFont;
        Assert.Equal("Consolas", font.Name);
        Assert.True(font.Size >= 11, $"the readout is drawn at only {font.Size} pt");
        var size = TextRenderer.MeasureText(view._time.Text, font, Size.Empty, TextFormatFlags.NoPadding);
        Assert.True(size.Width <= view._time.Width && size.Height <= view._time.Height, $"the readout, {size}, does not fit {view._time.Size}");
        Assert.True(view._seconds.Top >= view._time.Bottom);
        // The seek bar keeps room to seek, beside the jumps and the readout on its one row.
        Assert.True(view._seek.Width - 2 * Faderio.Shell.SeekBar.Inset > 60, $"the seek bar has only {view._seek.Width} px");
        Assert.Equal(view._seek.Parent, view._time.Parent!.Parent);

        panel.Size = new Size(900, 500); // dragged to the main panel: the readout at full size
        Assert.Equal(Faderio.Shell.Readout.LedSize, view._time.DrawnFont.Size);
    });
}
