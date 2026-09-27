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

    /// <summary>These settings as the engine keeps them: the current schema, a mode no version
    /// knows (or one from before schema 2) replaced by the default, and a blank library none.</summary>
    public MachineSettings Normalised() => this with
    {
        SchemaVersion = CurrentSchemaVersion,
        OutputMode = SchemaVersion >= 2 && Enum.IsDefined(OutputMode) ? OutputMode : AudioOutputMode.WasapiShared,
        Library = string.IsNullOrWhiteSpace(Library) ? null : Library,
    };
}

/// <summary>The machine's settings.json. Its recovery from a corrupt or newer file is
/// <see cref="SettingsFile{T}"/>'s.</summary>
public sealed class MachineSettingsStore : SettingsFile<MachineSettings>
{
    public MachineSettingsStore(string path)
        : base(path, "machine settings file", MachineSettings.CurrentSchemaVersion, s => s.Normalised())
    {
    }
}
