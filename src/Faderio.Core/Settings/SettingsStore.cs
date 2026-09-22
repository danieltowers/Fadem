using System.IO;
using System.Text.Json;
using Faderio.Core.Persistence;

namespace Faderio.Core.Settings;

/// <summary>
/// Loads and persists player settings as JSON per design.md D4. Missing keys fall back to
/// built-in defaults; a corrupt file produces a visible warning and a reset to defaults —
/// never silently swallowed. Writes are atomic (temp file + rename).
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,

        // Settings files are user-editable; accept any casing of the keys.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;

    public SettingsStore(string path) => _path = path;

    public SettingsLoadResult Load()
    {
        if (!File.Exists(_path))
        {
            return new SettingsLoadResult(new PlayerSettings(), null);
        }

        string json;
        try
        {
            json = File.ReadAllText(_path);
        }
        catch (IOException e)
        {
            return new SettingsLoadResult(new PlayerSettings(),
                $"Could not read settings file: {e.Message}. Defaults restored.");
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<PlayerSettings>(json, JsonOptions);
            var settings = loaded is null ? new PlayerSettings() : loaded;
            string? warning = null;
            if (settings.SchemaVersion > PlayerSettings.CurrentSchemaVersion)
            {
                warning = $"Settings were written by a newer faderio version (schema {settings.SchemaVersion}). Defaults restored.";
                settings = new PlayerSettings();
            }
            return new SettingsLoadResult(settings, warning);
        }
        catch (JsonException e)
        {
            // Corrupt settings file: warn visibly, reset to defaults, app still launches.
            return new SettingsLoadResult(new PlayerSettings(),
                $"Settings file is corrupt ({e.Message}). Defaults restored.");
        }
    }

    public void Save(PlayerSettings settings)
    {
        AtomicJson.Write(_path, settings, JsonOptions);
    }
}
