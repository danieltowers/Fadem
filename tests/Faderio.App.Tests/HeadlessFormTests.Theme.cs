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
