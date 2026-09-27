namespace Faderio.Core.Settings;

/// <summary>
/// The library's settings.json (ADR 0011): the player's settings, loaded once when the library
/// opens. Its recovery from a corrupt or newer file is <see cref="SettingsFile{T}"/>'s.
/// </summary>
public sealed class SettingsStore : SettingsFile<PlayerSettings>
{
    public SettingsStore(string path)
        : base(path, "settings file", PlayerSettings.CurrentSchemaVersion, s => s.Normalised())
    {
    }
}
