using System.Reflection;
using Faderio.Core.Playback;
using Faderio.Core.Settings;
using Faderio.Core.Tests;
using MusicBeePlugin;
using static MusicBeePlugin.Plugin;

namespace Faderio.MusicBee.Tests;

/// <summary>
/// The plugin's settings through MusicBee's plugin preferences (phase 5 issue 04): faderio's
/// Configure opens the dialog, and what its OK chose is sent when MusicBee saves its
/// preferences. The dialog is built for real and never shown: the test fills it in.
/// </summary>
public sealed partial class PluginTests
{
    private string OtherFolder => Path.Combine(_dir, "other");

    [Fact]
    public void Configure_OpensTheDialog_OnTheLibraryOpen_AndTheModeSaved()
    {
        AuthorLibrary();
        ChooseLibrary(LibraryFolder, AudioOutputMode.WaveOut);
        Start();
        Launched();

        string[]? listed = null;
        string[]? devices = null;
        var selected = -1;
        var opened = Configure(ok: false, read: dialog =>
        {
            listed = dialog._outputMode.Items.Cast<object>().Select(dialog._outputMode.GetItemText).ToArray();
            devices = dialog._outputDevice.Items.Cast<object>().Select(dialog._outputDevice.GetItemText).ToArray();
            selected = dialog._outputDevice.SelectedIndex;
        });

        Assert.Equal((LibraryFolder, AudioOutputMode.WaveOut, (string?)null), opened);
        Assert.Equal(new[] { "WASAPI shared", "WASAPI exclusive", "WaveOut" }, listed);
        // MusicBee's own device first (issue 13), then this machine's: nothing chosen is MusicBee's.
        Assert.Equal(new[] { "MusicBee's device", "Speakers (Test)", "DAC" }, devices);
        Assert.Equal(0, selected);
    }

    // ── The output device (phase 5 issue 13) ─────────────────────────────────────────────

    [Fact]
    public void WithNoDeviceChosen_TheOutputIsAskedForMusicBeesDevice_AtEachOpen()
    {
        AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        _musicBee.ActiveDevice = "Speakers (Test)";
        Start();
        Launched();

        Assert.Equal("Speakers (Test)", _deviceAsked!());
        Configure(ok: false, read: dialog => Assert.Equal("MusicBee's device (Speakers (Test))", dialog._outputDevice.Items[0]));

        _musicBee.ActiveDevice = "DAC"; // changed in MusicBee's preferences, followed
        Assert.Equal("DAC", _deviceAsked!());
        _musicBee.ActiveDevice = "Primary Sound Driver"; // MusicBee on Windows' default, DirectSound's name for it
        Assert.Null(_deviceAsked!());
        Configure(ok: false, read: dialog => Assert.Equal("MusicBee's device", dialog._outputDevice.Items[0]));
        _musicBee.ActiveDevice = "ASIO4ALL v2"; // a device faderio cannot open: Windows' default, no notice
        Assert.Null(_deviceAsked!());
    }

    [Fact]
    public void TheDeviceChosen_IsSavedAtSave_AskedOfTheOutput_ShownInTheStrip_AndReadBackByTheNextInitialise() => RunSta(() =>
    {
        AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        _musicBee.ActiveDevice = "Speakers (Test)";
        Start();
        Launched();
        _musicBee.Choose("Play");
        _output.Advance(4410);
        using var panel = new System.Windows.Forms.Panel();
        _plugin.OnDockablePanelCreated(panel);
        var view = View(panel);
        view.Render();
        Assert.Equal("library · WASAPI shared at 44100 Hz on Speakers (Test)", view._host.Text);

        Configure(device: "DAC");
        Assert.Equal("Speakers (Test)", _deviceAsked!()); // the choice waits for MusicBee's Save
        _plugin.SaveSettings();

        Assert.Equal(Transport.Playing, _output.State); // a device change stops nothing
        Assert.Equal("DAC", _deviceAsked!());
        Assert.Equal("DAC", new MachineSettingsStore(SettingsFile).Settings.OutputDevice);
        view.Render();
        Assert.Equal("library · WASAPI shared at 44100 Hz on DAC", view._host.Text);
        _ui.Pump();
        Assert.Equal("Saved machine settings.", view._status.Text); // in the strip, no box
        Assert.Empty(_ui.Messages);
        _plugin.Close(PluginCloseReason.MusicBeeClosing);

        Func<string?>? asked = null;
        var next = new Plugin((_, device, _) =>
        {
            asked = device;
            return new FakeAudioOutput();
        }, () => _devices);
        try
        {
            Start(next);
            Launched(next, LibraryFolder);
            Assert.Equal("DAC", asked!());
            string? opened = null;
            next.ShowSettings = dialog =>
            {
                opened = dialog.OutputDevice;
                return false;
            };
            next.Configure(IntPtr.Zero);
            Assert.Equal("DAC", opened);
        }
        finally
        {
            next.Close(PluginCloseReason.MusicBeeClosing);
        }
    });

    [Fact]
    public void ADeviceSavedThatIsNotThere_IsListedAsNotFound_AndKeptByAnUnchangedSave()
    {
        AuthorLibrary();
        ChooseLibrary(LibraryFolder, device: "Headphones");
        Start();
        Launched();

        string[]? devices = null;
        string? selected = null;
        var opened = Configure(read: dialog =>
        {
            devices = dialog._outputDevice.Items.Cast<object>().Select(dialog._outputDevice.GetItemText).ToArray();
            selected = dialog._outputDevice.GetItemText(dialog._outputDevice.SelectedItem);
        });
        _plugin.SaveSettings();

        Assert.Equal("Headphones", opened.Device);
        Assert.Equal(new[] { "MusicBee's device", "Speakers (Test)", "DAC", "Headphones (not found)" }, devices);
        Assert.Equal("Headphones (not found)", selected);
        Assert.Equal("Headphones", _deviceAsked!());
        Assert.Equal("Headphones", new MachineSettingsStore(SettingsFile).Settings.OutputDevice);
        _ui.Pump();
        Assert.Empty(_ui.Messages); // nothing was sent
    }

    [Fact]
    public void ChoosingMusicBeesDeviceAgain_SavesNone()
    {
        AuthorLibrary();
        ChooseLibrary(LibraryFolder, device: "DAC");
        Start();
        Launched();

        Configure(device: null, chooseMusicBees: true);
        _plugin.SaveSettings();

        Assert.Null(_deviceAsked!());
        Assert.Null(new MachineSettingsStore(SettingsFile).Settings.OutputDevice);
    }

    [Fact]
    public void SavedSettings_AreReadBackByTheNextInitialise()
    {
        AuthorLibrary();
        var (c, d) = AuthorLibrary(OtherFolder, "musicbee-other", 550, 770);
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();

        Configure(browsed: OtherFolder, mode: AudioOutputMode.WaveOut);
        _plugin.SaveSettings();
        Launched(_plugin, OtherFolder);
        _plugin.Close(PluginCloseReason.MusicBeeClosing);

        // MusicBee starts again: a new plugin on the same storage.
        var modes = new List<AudioOutputMode>();
        var next = new Plugin((mode, _, _) =>
        {
            modes.Add(mode);
            return new FakeAudioOutput();
        });
        try
        {
            Start(next);
            var view = Launched(next, OtherFolder);
            Assert.Equal(new[] { c, d }, view.Queue.Select(r => r.Track.FilePath));
            Assert.Equal(new[] { AudioOutputMode.WaveOut }, modes);
        }
        finally
        {
            next.Close(PluginCloseReason.MusicBeeClosing);
        }
    }

    [Fact]
    public void AnotherLibrary_WhilePlaying_StopsPlaybackAtSave_AndOpensItsOpenPlaylist()
    {
        AuthorLibrary();
        var (c, d) = AuthorLibrary(OtherFolder, "musicbee-other", 550, 770);
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        _musicBee.Choose("Play");
        _output.Advance(4410);

        Configure(typed: OtherFolder);
        Assert.Equal(Transport.Playing, _output.State); // the choice waits for MusicBee's Save

        _plugin.SaveSettings();

        Assert.Equal(Transport.Stopped, _output.State);
        var view = Launched(_plugin, OtherFolder);
        Assert.Equal(new LibraryView(OtherFolder, "other", IsAvailable: true), view.Library);
        Assert.Equal(new[] { c, d }, view.Queue.Select(r => r.Track.FilePath));
    }

    [Fact]
    public void AGoneFolder_IsAMessage_AndLeavesNoLibraryOpen()
    {
        AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        var gone = Path.Combine(_dir, "gone");

        Configure(typed: gone);
        _plugin.SaveSettings();
        _ui.Pump();

        var message = Assert.Single(_ui.Messages);
        Assert.Contains("gone", message);
        Assert.Contains("does not exist", message);
        var view = _plugin.Host!.Engine.View;
        Assert.Equal(new LibraryView(gone, "gone", IsAvailable: false), view.Library);
        Assert.Empty(view.Queue);
        _musicBee.Choose("Play");
        _output.Advance(4410);
        Assert.Equal(0, _output.FramesRendered);
    }

    [Fact]
    public void AFolderGoneAtStart_ChosenAgainOnceItIsBack_Opens()
    {
        ChooseLibrary(LibraryFolder);
        Start();
        _ui.Pump();
        Assert.Contains("does not exist", Assert.Single(_ui.Messages));
        var (a, b) = AuthorLibrary(); // the drive is back

        Assert.Equal(LibraryFolder, Configure().Library);
        _plugin.SaveSettings();

        var view = Launched();
        Assert.True(view.Library!.IsAvailable);
        Assert.Equal(new[] { a, b }, view.Queue.Select(r => r.Track.FilePath));
    }

    [Fact]
    public void Cancel_SendsNothing_AndNorDoesASaveWithNothingChosen()
    {
        AuthorLibrary();
        AuthorLibrary(OtherFolder, "musicbee-other", 550, 770);
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        _musicBee.Choose("Play");
        _output.Advance(4410);

        Configure(typed: OtherFolder, mode: AudioOutputMode.WaveOut, ok: false);
        _plugin.SaveSettings();
        _plugin.SaveSettings();

        Assert.Equal(Transport.Playing, _output.State);
        var view = _plugin.Host!.Engine.View;
        Assert.Equal(LibraryFolder, view.Library!.Path);
        Assert.Equal(AudioOutputMode.WasapiShared, view.MachineSettings.OutputMode);
        _ui.Pump();
        Assert.Empty(_ui.Messages);
    }

    [Fact]
    public void AnotherModeOnly_KeepsPlaying_AndIsSavedForTheNextStart()
    {
        AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        _musicBee.Choose("Play");
        _output.Advance(4410);

        Configure(mode: AudioOutputMode.WasapiExclusive);
        _plugin.SaveSettings();

        Assert.Equal(Transport.Playing, _output.State);
        var saved = new MachineSettingsStore(SettingsFile).Settings;
        Assert.Equal(new MachineSettings { Library = LibraryFolder, OutputMode = AudioOutputMode.WasapiExclusive }, saved);
    }

    [Fact]
    public void Configure_WhenTheEngineCouldNotStart_RepeatsWhy_AndOpensNoDialog()
    {
        // The engine's start fails as a missing dependency would: the output cannot be built.
        var broken = new Plugin((_, _, _) => throw new FileNotFoundException("Could not load file or assembly 'Faderio.Core'"));
        var shown = false;
        broken.ShowSettings = _ =>
        {
            shown = true;
            return true;
        };
        Start(broken);

        Assert.True(broken.Configure(IntPtr.Zero));

        Assert.False(shown);
        Assert.Equal(2, _ui.Messages.Count(m => m.StartsWith("faderio could not start", StringComparison.Ordinal)));
        broken.SaveSettings();
    }

    /// <summary>Chooses faderio's Configure in MusicBee's plugin preferences: the dialog is filled
    /// in (a folder typed or browsed to, a mode picked) and OK or Cancel pressed. Returns what
    /// the dialog opened on.</summary>
    /// <param name="device">A device to pick by name, or with <paramref name="chooseMusicBees"/> MusicBee's own.</param>
    private (string? Library, AudioOutputMode Mode, string? Device) Configure(
        string? typed = null, string? browsed = null, AudioOutputMode? mode = null, bool ok = true,
        Action<SettingsDialog>? read = null, string? device = null, bool chooseMusicBees = false)
    {
        (string?, AudioOutputMode, string?)? opened = null;
        _plugin.ShowSettings = dialog =>
        {
            opened = (dialog.Library, dialog.OutputMode, dialog.OutputDevice);
            read?.Invoke(dialog);
            if (chooseMusicBees)
            {
                dialog._outputDevice.SelectedIndex = 0;
            }
            else if (device is not null)
            {
                dialog._outputDevice.SelectedItem = device;
            }
            if (typed is not null)
            {
                dialog._library.Text = typed;
            }
            if (browsed is not null)
            {
                dialog.ChooseFolder = _ => browsed;
                Click(dialog._browse);
            }
            if (mode is { } picked)
            {
                dialog._outputMode.SelectedItem = picked;
            }
            return ok;
        };

        Assert.True(_plugin.Configure(IntPtr.Zero)); // handled: MusicBee shows nothing of its own

        Assert.True(opened.HasValue, "Configure opened no dialog.");
        return opened!.Value;
    }

    /// <summary>Clicks a button of a dialog never shown, where <c>PerformClick</c> does nothing
    /// (as HeadlessFormTests does).</summary>
    private static void Click(System.Windows.Forms.ButtonBase button)
    {
        Assert.True(button.Enabled, $"{button.Text} is disabled");
        var onClick = typeof(System.Windows.Forms.Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        onClick.Invoke(button, new object[] { EventArgs.Empty });
    }
}
