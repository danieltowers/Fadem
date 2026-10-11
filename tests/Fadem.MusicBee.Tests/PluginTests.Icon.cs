using Fadem.Core.Domain;
using Fadem.Core.Settings;
using Fadem.Shell;

namespace Fadem.MusicBee.Tests;

/// <summary>The Fadem icon (2026-10-11) on the plugin's Configure… dialog, not MusicBee's.</summary>
public sealed partial class PluginTests
{
    [Fact]
    public void TheSettingsDialog_WearsTheFademIcon() => RunSta(() =>
    {
        using var dialog = new SettingsDialog(
            null, AudioOutputMode.WasapiShared, null, Array.Empty<string>(), null, false,
            false, false, 5, 5, ReplayGainMode.Track, FadeCurveKind.EqualPower);

        Assert.Same(AppIcon.Shared, dialog.Icon);
    });
}
