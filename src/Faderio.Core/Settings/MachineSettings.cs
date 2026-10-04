using System.Text.Json.Serialization;
using Faderio.Core.Persistence;

namespace Faderio.Core.Settings;

public enum AudioOutputMode
{
    /// <summary>WASAPI shared (default): playback alongside other apps.</summary>
    WasapiShared,

    /// <summary>WASAPI exclusive (opted in): bit-perfect output, takes the device.</summary>
    WasapiExclusive,

    /// <summary>WaveOut: the compatibility fallback, and the third mode the slice must prove on.</summary>
    WaveOut,
}

/// <summary>
/// What belongs to this machine rather than to a library (ADR 0011, D11), persisted as JSON in
/// <c>%APPDATA%\faderio\settings.json</c>: the output mode and which library to open. Keys it
/// does not know are ignored and not carried over (D13).
/// </summary>
public sealed record MachineSettings
{
    /// <summary>2 since the output mode moved here (library issue 03). A file of schema 1 is
    /// either issue 02's, which had no output mode, or the player's settings from before the
    /// library, whose output mode is not migrated (D13): its library stays, its mode is the
    /// default.</summary>
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>The output to open at the next launch: the engine rate is fixed at launch
    /// (ADR 0008), so a change waits for it.</summary>
    public AudioOutputMode OutputMode { get; init; } = AudioOutputMode.WasapiShared;

    /// <summary>The folder of the last library that opened, or null before any has.</summary>
    public string? Library { get; init; }

    /// <summary>The other libraries that have opened on this machine for this host, most recently
    /// opened first (library chooser issue 01): with <see cref="Library"/> before them, the
    /// known libraries the chooser lists. Never <see cref="Library"/> itself, so a file from
    /// before the key reads as knowing only that one. Schema 2 still.</summary>
    public IReadOnlyList<string> OtherLibraries { get; init; } = [];

    /// <summary>The known libraries, most recently opened first.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> KnownLibraries => Library is { } library ? [library, .. OtherLibraries] : OtherLibraries;

    /// <summary>The output device by its name in Windows, or null for the host's default
    /// (phase 5 issue 13): standalone faderio opens Windows' default device and leaves this
    /// null; the plugin follows MusicBee's own device. The plugin's output opens the device at
    /// every Play from stopped, so it applies there. Schema 2 still: a file without the key
    /// reads as null.</summary>
    public string? OutputDevice { get; init; }

    /// <summary>These settings as the engine keeps them: the current schema, a mode no version
    /// knows (or one from before schema 2) replaced by the default, a blank library none, and
    /// the other libraries without blanks, repeats or the library itself.</summary>
    public MachineSettings Normalised()
    {
        var library = string.IsNullOrWhiteSpace(Library) ? null : Library;
        var others = new List<string>();
        foreach (var folder in OtherLibraries ?? [])
        {
            if (!string.IsNullOrWhiteSpace(folder) && !LibraryFolders.Same(folder, library) && !others.Any(o => LibraryFolders.Same(o, folder)))
            {
                others.Add(folder);
            }
        }
        return this with
        {
            SchemaVersion = CurrentSchemaVersion,
            OutputMode = SchemaVersion >= 2 && Compat.IsDefined(OutputMode) ? OutputMode : AudioOutputMode.WasapiShared,
            Library = library,
            OtherLibraries = others,
            OutputDevice = string.IsNullOrWhiteSpace(OutputDevice) ? null : OutputDevice,
        };
    }

    /// <summary>These settings with <paramref name="folder"/> as the library that opened last:
    /// the one before it first among the others.</summary>
    public MachineSettings Opened(string folder) =>
        (this with { Library = folder, OtherLibraries = Library is { } previous ? [previous, .. OtherLibraries] : OtherLibraries }).Normalised();

    /// <summary>These settings without <paramref name="folder"/> among the known libraries: the
    /// library a launch reopens too, when it is that one.</summary>
    public MachineSettings Forgotten(string folder) => this with
    {
        Library = LibraryFolders.Same(Library, folder) ? null : Library,
        OtherLibraries = OtherLibraries.Where(o => !LibraryFolders.Same(o, folder)).ToList(),
    };

    // The list is compared by its folders, not as a reference: settings read back equal those saved.
    public bool Equals(MachineSettings? other) =>
        other is not null && SchemaVersion == other.SchemaVersion && OutputMode == other.OutputMode &&
        Library == other.Library && OutputDevice == other.OutputDevice && OtherLibraries.SequenceEqual(other.OtherLibraries);

    public override int GetHashCode() => (SchemaVersion, OutputMode, Library, OutputDevice, OtherLibraries.Count).GetHashCode();
}

/// <summary>The machine's settings.json. Its recovery from a corrupt or newer file is
/// <see cref="SettingsFile{T}"/>'s.</summary>
public sealed class MachineSettingsStore : SettingsFile<MachineSettings>
{
    public MachineSettingsStore(string path)
        : base(path, "machine settings file", MachineSettings.CurrentSchemaVersion, s => s.Normalised())
    {
    }

    /// <summary>Takes <paramref name="folder"/> off the known libraries and saves (library
    /// chooser issue 01): the engine's <c>ForgetLibrary</c>, and a launch's before the engine
    /// exists. A folder not among them is no write. Throws as <see cref="SettingsFile{T}.Save"/>.</summary>
    /// <returns>Whether it was among them.</returns>
    public bool Forget(string folder)
    {
        if (!Settings.KnownLibraries.Any(known => LibraryFolders.Same(known, folder)))
        {
            return false;
        }
        Save(Settings.Forgotten(folder));
        return true;
    }
}
