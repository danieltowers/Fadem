using Faderio.Core.Persistence;
using Faderio.Core.Playback;
using Faderio.Core.Settings;
using Faderio.Shell.Audio;

namespace Faderio.MusicBee;

/// <summary>
/// The plugin's composition, as standalone faderio's composition root builds it: the output,
/// the library its settings name, a sink for the notices and the machine settings store. Its
/// settings are its own, in a folder of MusicBee's persistent storage, so the two hosts never
/// overwrite each other's output mode or library.
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
            boxes.Show("No faderio library is chosen, so there is nothing to play. " +
                       $"Name one as \"library\" in {settingsPath}.");
        }
        var library = folder is null ? null : Library.Open(folder);

        // Only the output mode is read here: the output opens before the engine that runs at its
        // rate (ADR 0008).
        var mode = machine.Settings.OutputMode;
        var output = outputs?.Invoke(mode, sink.Notice) ?? new NAudioAudioOutput(mode, sink.Notice);
        Engine = new PlaybackEngine(output, library, sink, machine: machine);
    }

    public PlaybackEngine Engine { get; }

    public void Play() => Engine.Play();

    public void Stop() => Engine.Stop();

    public void Dispose() => Engine.Dispose();
}
