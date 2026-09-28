using Faderio.Core.Persistence;
using Faderio.Core.Playback;
using Faderio.Core.Settings;
using Faderio.Shell.Audio;

namespace Faderio.MusicBee;

/// <summary>
/// The plugin's composition, as standalone faderio's composition root builds it: the output,
/// the library its settings name, a sink for the notices and the machine settings store. Its
/// settings are its own, in a folder of MusicBee's persistent storage, so the two hosts never
/// overwrite each other's output mode or library. MusicBee's plugin preferences change them:
/// <see cref="Configure"/> takes the dialog's choice, and <see cref="SaveSettings"/> sends it.
/// </summary>
internal sealed class Host : IDisposable
{
    /// <param name="outputs">The output to open for a mode, for tests; null opens the device.</param>
    public Host(string settingsFolder, MessageBoxes boxes, Func<AudioOutputMode, Action<string>, IAudioOutput>? outputs)
    {
        // The engine's notices, as message boxes; the spike has no view to render. Core's own sink:
        // a plugin type implementing a Core interface could not load before the resolver exists.
        var sink = new ImmediateSink(notice: boxes.Show);
        Directory.CreateDirectory(settingsFolder);
        var settingsPath = Path.Combine(settingsFolder, "settings.json");
        var machine = new MachineSettingsStore(settingsPath);

        // A folder that is gone is opened anyway: the engine says so. None chosen is said here.
        var folder = machine.Settings.Library;
        if (folder is null)
        {
            boxes.Show("No faderio library is chosen, so there is nothing to play. Choose one in " +
                       "MusicBee's Preferences, under Plugins: faderio's Configure.");
        }
        var library = folder is null ? null : Library.Open(folder);

        // Only the output mode is read here: the output opens before the engine that runs at its
        // rate (ADR 0008). A guest in MusicBee's process lets go of the device while stopped, so
        // MusicBee's own player has it back after faderio's Stop (issue 06, decision A).
        var mode = machine.Settings.OutputMode;
        var output = outputs?.Invoke(mode, sink.Notice)
                     ?? new NAudioAudioOutput(mode, sink.Notice, releaseWhileStopped: true);
        Engine = new PlaybackEngine(output, library, sink, machine: machine);
    }

    /// <summary>What the settings dialog's OK chose, until MusicBee's Save sends it; null when
    /// nothing waits. Not the engine's: the user's choice, not yet applied.</summary>
    private MachineSettings? _chosen;

    public PlaybackEngine Engine { get; }

    public void Play() => Engine.Play();

    public void Stop() => Engine.Stop();

    /// <summary>Opens the settings dialog on the choice waiting to be saved, or else on the
    /// library open (or the one remembered) and the output mode saved. OK keeps its choice for
    /// <see cref="SaveSettings"/>, as MusicBee's own preferences wait for Save.</summary>
    /// <param name="show">Shows the dialog and says whether the user pressed OK.</param>
    public void Configure(Func<SettingsDialog, bool> show)
    {
        var view = Engine.View;
        var shown = _chosen ?? view.MachineSettings with { Library = view.Library?.Path ?? view.MachineSettings.Library };
        using var dialog = new SettingsDialog(shown.Library, shown.OutputMode);
        if (show(dialog))
        {
            _chosen = new MachineSettings { Library = dialog.Library, OutputMode = dialog.OutputMode };
        }
    }

    /// <summary>Sends what the dialog chose, each change to its own engine command, as standalone
    /// faderio's Open library and Settings do: another library stops playback and opens that
    /// library's open playlist, remembered for the next start; another output mode is saved for
    /// the next start. One the same as now sends nothing, so an Apply that follows another
    /// preference's change never stops faderio.</summary>
    public void SaveSettings()
    {
        if (_chosen is not { } chosen)
        {
            return;
        }
        _chosen = null;
        var view = Engine.View;
        if (chosen.Library is { } folder && !IsOpen(view.Library, folder))
        {
            Engine.OpenLibrary(folder);
        }
        if (chosen.OutputMode != view.MachineSettings.OutputMode)
        {
            Engine.ChangeMachineSettings(chosen);
        }
    }

    public void Dispose() => Engine.Dispose();

    /// <summary>Whether <paramref name="folder"/> is the library open now, and opened: one that
    /// could not open is tried again.</summary>
    private static bool IsOpen(LibraryView? open, string folder) =>
        open is { IsAvailable: true } && string.Equals(open.Path, folder, StringComparison.OrdinalIgnoreCase);
}
