using System.Drawing;
using System.Windows.Forms;
using Faderio.Shell.Theme;

namespace Faderio.MusicBee.Tests;

/// <summary>Studio theme issue 06: the plugin's Configure dialog, menus and status strip in the
/// skin's palette.</summary>
public sealed partial class PluginTests
{
    [Fact]
    public void TheViewsMenus_AreDrawnInTheSkinsPalette_AndTheNoticeLinkIsLit() => RunSta(() =>
    {
        AnswerDuophonic();
        using var panel = new Panel { Size = new Size(900, 500) };
        var view = PaletteView(panel);

        foreach (var menu in new[] { view._queueList.ContextMenuStrip!, view._columns.Menu, view._playlists._list.ContextMenuStrip! })
        {
            var renderer = Assert.IsType<StudioRenderer>(menu.Renderer);
            Assert.Same(view.Palette, renderer.Palette);
        }
        Assert.Equal("#CD5C5C", Palette.Hex(view._noticeCount.LinkColor));
    });

    [Fact]
    public void Configure_IsInTheSkinsColours_WithNoSystemColourLeft() => RunSta(() =>
    {
        AnswerDuophonic();
        AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        IEnumerable<Control> All(Control c) => new[] { c }.Concat(c.Controls.Cast<Control>().SelectMany(All));
        string[]? systemColoured = null;
        Color? ground = null;

        Configure(ok: false, read: dialog =>
        {
            systemColoured = All(dialog).Where(c => c.BackColor.IsSystemColor || c.ForeColor.IsSystemColor)
                .Select(c => $"{c.GetType().Name} '{c.Text}'").ToArray();
            ground = dialog.BackColor;
        });

        Assert.Empty(systemColoured!);
        Assert.Equal("#1C1C1C", Palette.Hex(ground!.Value)); // MusicBee's own bars
    });
}
