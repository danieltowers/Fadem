using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Faderio.Core.Domain;
using Faderio.Core.Persistence;

namespace Faderio.Core.Transitions;

/// <summary>
/// The JSON transition store per ADR 0003: a single transitions.json, schemaVersion first,
/// transitions keyed by the ordered path pair (outgoing|incoming), a per-playlist overrides
/// section, loaded into memory at startup and persisted atomically on change.
/// </summary>
public sealed class JsonTransitionStore : ITransitionStore
{
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Curve by name, not ordinal. Integer values still deserialise, so v2's files load.
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private StoreFileModel _model = new();

    public JsonTransitionStore(string path)
    {
        _path = path;
        Load();
    }

    public TransitionSpec? Get(string outgoingPath, string incomingPath)
    {
        return _model.Transitions.TryGetValue(PairKey(outgoingPath, incomingPath), out var spec)
            ? spec
            : null;
    }

    public void Upsert(string outgoingPath, string incomingPath, TransitionSpec spec)
    {
        _model.Transitions[PairKey(outgoingPath, incomingPath)] = spec;
        Persist();
    }

    public bool Remove(string outgoingPath, string incomingPath)
    {
        var removed = _model.Transitions.Remove(PairKey(outgoingPath, incomingPath));
        if (removed)
        {
            Persist();
        }
        return removed;
    }

    public TransitionSpec? GetOverride(string playlistName, string outgoingPath, string incomingPath)
    {
        if (!_model.PlaylistOverrides.TryGetValue(playlistName, out var byPair))
        {
            return null;
        }
        return byPair.TryGetValue(PairKey(outgoingPath, incomingPath), out var spec) ? spec : null;
    }

    public void UpsertOverride(string playlistName, string outgoingPath, string incomingPath, TransitionSpec partial)
    {
        var byPair = _model.PlaylistOverrides.TryGetValue(playlistName, out var existing)
            ? existing
            : new Dictionary<string, TransitionSpec>();
        byPair[PairKey(outgoingPath, incomingPath)] = partial;
        _model.PlaylistOverrides[playlistName] = byPair;
        Persist();
    }

    public bool RemoveOverride(string playlistName, string outgoingPath, string incomingPath)
    {
        if (!_model.PlaylistOverrides.TryGetValue(playlistName, out var byPair) ||
            !byPair.Remove(PairKey(outgoingPath, incomingPath)))
        {
            return false;
        }
        if (byPair.Count == 0)
        {
            _model.PlaylistOverrides.Remove(playlistName);
        }
        Persist();
        return true;
    }

    internal static string PairKey(string outgoingPath, string incomingPath) =>
        Normalize(outgoingPath) + "|" + Normalize(incomingPath);

    internal static string Normalize(string path) => Path.GetFullPath(path);

    private void Load()
    {
        if (!File.Exists(_path))
        {
            _model = new StoreFileModel();
            return;
        }

        var json = File.ReadAllText(_path);
        StoreFileModel model;
        try
        {
            model = JsonSerializer.Deserialize<StoreFileModel>(json, JsonOptions)
                    ?? throw new InvalidDataException("Transition store file is empty.");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"Transition store file is corrupt: {e.Message}", e);
        }

        if (model.SchemaVersion > SchemaVersion)
        {
            // Detected and reported, never silently ignored.
            throw new TransitionStoreVersionException(model.SchemaVersion, SchemaVersion);
        }

        _model = model;
    }

    private void Persist()
    {
        AtomicJson.Write(_path, _model, JsonOptions);
    }
}

/// <summary>On-disk model. schemaVersion is declared first so it leads the file (ADR 0003).</summary>
internal sealed class StoreFileModel
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = JsonTransitionStore.SchemaVersion;

    [JsonPropertyName("transitions")]
    public Dictionary<string, TransitionSpec> Transitions { get; set; } = new();

    [JsonPropertyName("playlistOverrides")]
    public Dictionary<string, Dictionary<string, TransitionSpec>> PlaylistOverrides { get; set; } = new();
}
