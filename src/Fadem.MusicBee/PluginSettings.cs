using System.Text.Json;
using Fadem.Core.Persistence;

namespace Fadem.MusicBee;

/// <summary>
/// What only the plugin has a use for (phase 6 issue 08), kept out of the machine settings that
/// standalone Fadem shares: whether Fadem takes over MusicBee's Now Playing tab. Its own file,
/// <c>plugin.json</c>, beside the plugin's <c>settings.json</c>.
/// </summary>
internal sealed class PluginSettings
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Whether MusicBee's Now Playing tab shows Fadem in place of its own view.</summary>
    public bool NowPlayingTab { get; set; }
}

/// <summary>
/// The plugin's own settings file, loaded once when opened. A missing key is its default; a file
/// that cannot be read or parsed loads the defaults, says so, and is left as it is until a
/// setting is saved, when it is overwritten. Writes are atomic. Simpler than Core's
/// <c>SettingsFile</c>, which this assembly cannot derive from (LoadingTests): one flag, no
/// schema migration, nothing moved aside.
/// <para>No field holds a type of System.Text.Json or Core: MusicBee loads every type here
/// before the plugin's resolver exists (LoadingTests).</para>
/// </summary>
internal sealed class PluginSettingsFile
{
    private readonly string _path;

    /// <param name="notice">Where a file that could not be loaded is said.</param>
    public PluginSettingsFile(string path, Action<string> notice)
    {
        _path = path;
        Settings = Load(notice);
    }

    /// <summary>The settings loaded, or last saved.</summary>
    public PluginSettings Settings { get; private set; }

    /// <summary>Writes the settings, and keeps them only once they are on disk.</summary>
    public void Save(PluginSettings settings)
    {
        AtomicJson.Write(_path, settings, Options());
        Settings = settings;
    }

    private PluginSettings Load(Action<string> notice)
    {
        if (!File.Exists(_path))
        {
            return new PluginSettings();
        }
        try
        {
            return JsonSerializer.Deserialize<PluginSettings>(File.ReadAllText(_path), Options()) ?? new PluginSettings();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            notice($"Fadem's plugin settings file could not be read ({e.Message}). Defaults are used, " +
                   "and the file is left as it is until a setting is changed.");
            return new PluginSettings();
        }
    }

    /// <summary>As the machine settings file's: keys in any casing, written camelCase and indented.</summary>
    private static JsonSerializerOptions Options() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };
}
