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

        Assert.Equal("#232323", Palette.Hex(view.Palette.Chrome));
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
}
