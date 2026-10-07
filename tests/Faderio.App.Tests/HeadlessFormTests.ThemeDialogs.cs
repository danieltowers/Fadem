using Faderio.Core.Settings;
using Faderio.Shell.Theme;

namespace Faderio.App.Tests;

/// <summary>Studio theme issue 06: the App's dialogs, menus and status strip in its palette.</summary>
public sealed partial class HeadlessFormTests
{
    [Fact]
    public void EveryMenu_IsDrawnInTheFormsPalette_AndTheNoticeLinkIsLit() => RunSta(() =>
    {
        using var h = new Harness();

        foreach (var menu in new[] { h.Form._queueList.ContextMenuStrip!, h.Form._columnMenu, h.Form._playlists._list.ContextMenuStrip! })
        {
            var renderer = Assert.IsType<StudioRenderer>(menu.Renderer);
            Assert.Same(h.Form.Palette, renderer.Palette);
        }
        Assert.Equal(h.Form.Palette.Accent, h.Form._noticeCount.LinkColor);
    });

    [Fact]
    public void TheSettings_MadeWithAPalette_KeepNoSystemColour() => RunSta(() =>
    {
        var duophonic = Palette.From(new SkinColours(
            unchecked((int)0xFF1C1C1C), unchecked((int)0xFF121212), 0, unchecked((int)0xFF2C2C2C),
            unchecked((int)0xFFE1E1E1), unchecked((int)0xFF6D6D6D), unchecked((int)0xFFCD5C5C)));
        using var settings = new SettingsForm(new MachineSettings(), new PlayerSettings(), null, duophonic);

        Assert.Empty(AllControls(settings).Where(c => c.BackColor.IsSystemColor || c.ForeColor.IsSystemColor)
            .Select(c => $"{c.GetType().Name} '{c.Text}'"));
        Assert.Equal(duophonic.Chrome, settings.BackColor);
        Assert.Equal(duophonic.Dim, settings._nextLaunch.ForeColor);
        Assert.Equal(duophonic.Well, settings._seconds.BackColor);
    });
}
