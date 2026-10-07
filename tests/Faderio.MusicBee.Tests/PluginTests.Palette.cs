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
        _musicBee.Skin[(7, 4, 3)] = Opaque(0xCD5C5C);
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
        Assert.Equal("#CD5C5C", Palette.Hex(view.Palette.Accent));
        Assert.True(Palette.Load(file)!.Same(view.Palette));
    });

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
    public void InTheSidebar_TheLedReadoutIsWholeAndInTheAccent_AndTheSeekRowHoldsOneLine() => RunSta(() =>
    {
        AnswerDuophonic();
        using var panel = new Panel { Size = new Size(285, 700) }; // MusicBee's sidebar, as shipped
        var view = PaletteView(panel);
        Click(view._playPause);
        view.Render();

        Assert.Equal(view.Palette.Accent, view._time.ForeColor);
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
