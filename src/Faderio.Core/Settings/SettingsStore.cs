using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Faderio.Core.Persistence;

namespace Faderio.Core.Settings;

/// <summary>
/// settings.json, loaded once when opened, as the transition store is (wayfinder ticket 10).
/// Missing keys fall back to built-in defaults. A corrupt file is moved aside to
/// <c>settings.json.corrupt</c>, so the old settings stay recoverable; a file from a newer
/// schema is left alone. Either way defaults load and <see cref="LoadNotice"/> says so: the
/// engine, which owns this store, raises it. Writes are atomic (temp file + rename).
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,

        // Settings files are user-editable: keys in any casing, choices by name. Integer values
        // still deserialise.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private const string LeftAsItIs = "Defaults are used, and the file is left as it is until a setting is changed.";

    private readonly string _path;

    public SettingsStore(string path)
    {
        _path = path;
        (Settings, LoadNotice) = Load();
    }

    /// <summary>The settings loaded, or last saved.</summary>
    public PlayerSettings Settings { get; private set; }

    /// <summary>What went wrong opening the file, worded for the user; null when nothing did.</summary>
    public string? LoadNotice { get; }

    /// <summary>Writes the settings, and keeps them only once they are on disk.</summary>
    public void Save(PlayerSettings settings)
    {
        AtomicJson.Write(_path, settings, JsonOptions);
        Settings = settings;
    }

    private (PlayerSettings, string?) Load()
    {
        if (!File.Exists(_path))
        {
            return (new PlayerSettings(), null);
        }

        string json;
        try
        {
            json = File.ReadAllText(_path);
        }
        catch (Exception e) when (IsFileFailure(e))
        {
            return (new PlayerSettings(), $"The settings file could not be read ({e.Message}). Defaults are used.");
        }

        PlayerSettings? loaded;
        try
        {
            // The schema first, on its own: a newer version may hold values this one cannot
            // parse, and its file must not be taken for a corrupt one.
            if (SchemaVersionOf(json) is > PlayerSettings.CurrentSchemaVersion and var newer)
            {
                return (new PlayerSettings(),
                    $"The settings file was written by a newer faderio (schema {newer}). {LeftAsItIs}");
            }
            loaded = JsonSerializer.Deserialize<PlayerSettings>(json, JsonOptions);
        }
        catch (JsonException e)
        {
            return (new PlayerSettings(), MoveAside(e.Message));
        }
        return (loaded?.Normalised() ?? new PlayerSettings(), null);
    }

    /// <summary>The file's schemaVersion, in any casing, or null when it has none.</summary>
    private static int? SchemaVersionOf(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name, "schemaVersion", StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.TryGetInt32(out var version) ? version : null;
            }
        }
        return null;
    }

    private string MoveAside(string problem)
    {
        var name = Path.GetFileName(_path);
        try
        {
            File.Move(_path, _path + ".corrupt", overwrite: true);
        }
        catch (Exception e) when (IsFileFailure(e))
        {
            return $"The settings file is corrupt ({problem}) and could not be moved aside ({e.Message}). {LeftAsItIs}";
        }
        return $"The settings file is corrupt ({problem}). It was moved aside to {name}.corrupt and defaults are used.";
    }

    private static bool IsFileFailure(Exception e) => e is IOException or UnauthorizedAccessException;
}
