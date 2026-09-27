namespace Faderio.Core.Settings;

/// <summary>
/// What belongs to this machine rather than to a library (ADR 0011), persisted as JSON in
/// <c>%APPDATA%\faderio\settings.json</c>. For now only which library to open (library issue
/// 02); library issue 03 moves the output mode here. Keys it does not know, left by the file this
/// one replaces, are ignored and not carried over (D13).
/// </summary>
public sealed record MachineSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>The folder of the last library that opened, or null before any has.</summary>
    public string? Library { get; init; }

    public MachineSettings Normalised() => this with
    {
        SchemaVersion = CurrentSchemaVersion,
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
