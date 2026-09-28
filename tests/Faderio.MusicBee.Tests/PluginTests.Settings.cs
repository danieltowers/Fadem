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
        var opened = Configure(ok: false, read: dialog =>
            listed = dialog._outputMode.Items.Cast<object>().Select(dialog._outputMode.GetItemText).ToArray());

        Assert.Equal((LibraryFolder, AudioOutputMode.WaveOut), opened);
        Assert.Equal(new[] { "WASAPI shared", "WASAPI exclusive", "WaveOut" }, listed);
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
        var next = new Plugin((mode, _) =>
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
        var broken = new Plugin((_, _) => throw new FileNotFoundException("Could not load file or assembly 'Faderio.Core'"));
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
    private (string? Library, AudioOutputMode Mode) Configure(
        string? typed = null, string? browsed = null, AudioOutputMode? mode = null, bool ok = true,
        Action<SettingsDialog>? read = null)
    {
        (string?, AudioOutputMode)? opened = null;
        _plugin.ShowSettings = dialog =>
        {
            opened = (dialog.Library, dialog.OutputMode);
            read?.Invoke(dialog);
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
