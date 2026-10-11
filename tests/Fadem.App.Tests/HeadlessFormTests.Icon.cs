using Fadem.Core.Settings;
using Fadem.Shell;

namespace Fadem.App.Tests;

/// <summary>The Fadem icon (2026-10-11) on the App's own windows: the main window's title bar and
/// taskbar button, and the Settings dialog's.</summary>
public sealed partial class HeadlessFormTests
{
    [Fact]
    public void TheMainWindow_WearsTheFademIcon() => RunSta(() =>
    {
        using var h = new Harness();

        Assert.Same(AppIcon.Shared, h.Form.Icon);
    });

    [Fact]
    public void TheSettings_WearTheFademIcon() => RunSta(() =>
    {
        using var settings = new SettingsForm(new MachineSettings(), new PlayerSettings(), null);

        Assert.Same(AppIcon.Shared, settings.Icon);
    });
}
