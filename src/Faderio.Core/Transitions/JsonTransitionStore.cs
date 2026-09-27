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

    public void Upsert(string outgoingPath, string incomingPath, TransitionSpec spec) =>
        Commit(model => model.Transitions[PairKey(outgoingPath, incomingPath)] = spec);

    public bool Remove(string outgoingPath, string incomingPath)
    {
        var key = PairKey(outgoingPath, incomingPath);
        if (!_model.Transitions.ContainsKey(key))
        {
            return false;
        }
        Commit(model => model.Transitions.Remove(key));
        return true;
    }

    public IReadOnlyList<(string Outgoing, string Incoming)> Pairs() => _model.Transitions.Keys.Select(Unkey).ToList();

    public IReadOnlyList<(string Outgoing, string Incoming)> OverridePairs(string playlistName) =>
        _model.PlaylistOverrides.TryGetValue(playlistName, out var byPair) ? byPair.Keys.Select(Unkey).ToList() : [];

    public TransitionSpec? GetOverride(string playlistName, string outgoingPath, string incomingPath)
    {
        if (!_model.PlaylistOverrides.TryGetValue(playlistName, out var byPair))
        {
            return null;
        }
        return byPair.TryGetValue(PairKey(outgoingPath, incomingPath), out var spec) ? spec : null;
    }

    public void UpsertOverride(string playlistName, string outgoingPath, string incomingPath, TransitionSpec partial) =>
        Commit(model =>
        {
            var byPair = model.PlaylistOverrides.TryGetValue(playlistName, out var existing)
                ? existing
                : new Dictionary<string, TransitionSpec>();
            byPair[PairKey(outgoingPath, incomingPath)] = partial;
            model.PlaylistOverrides[playlistName] = byPair;
        });

    public bool RemoveOverride(string playlistName, string outgoingPath, string incomingPath)
    {
        var key = PairKey(outgoingPath, incomingPath);
        if (!_model.PlaylistOverrides.TryGetValue(playlistName, out var existing) || !existing.ContainsKey(key))
        {
            return false;
        }
        Commit(model =>
        {
            var byPair = model.PlaylistOverrides[playlistName];
            byPair.Remove(key);
            if (byPair.Count == 0)
            {
                model.PlaylistOverrides.Remove(playlistName);
            }
        });
        return true;
    }

    public void RenamePlaylist(string oldName, string newName)
    {
        if (oldName == newName ||
            (!_model.PlaylistOverrides.ContainsKey(oldName) && !_model.PlaylistOverrides.ContainsKey(newName)))
        {
            return;
        }
        Commit(model =>
        {
            model.PlaylistOverrides.Remove(newName);
            if (model.PlaylistOverrides.Remove(oldName, out var byPair))
            {
                model.PlaylistOverrides[newName] = byPair;
            }
        });
    }

    public void RemovePlaylist(string playlistName)
    {
        if (_model.PlaylistOverrides.ContainsKey(playlistName))
        {
            Commit(model => model.PlaylistOverrides.Remove(playlistName));
        }
    }

    internal static string PairKey(string outgoingPath, string incomingPath) =>
        Normalize(outgoingPath) + "|" + Normalize(incomingPath);

    /// <summary>A key back into its two paths. A Windows path holds no '|'.</summary>
    private static (string Outgoing, string Incoming) Unkey(string key)
    {
        var bar = key.IndexOf('|');
        return (key[..bar], key[(bar + 1)..]);
    }

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

    /// <summary>Applies a change to a copy and keeps the copy only once it is on disk, so a
    /// write that fails changes nothing and the caller can truthfully say "not saved".</summary>
    private void Commit(Action<StoreFileModel> change)
    {
        var next = _model.Copy();
        change(next);
        AtomicJson.Write(_path, next, JsonOptions);
        _model = next;
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

    /// <summary>A copy whose dictionaries can change without touching this one. The specs are
    /// immutable records, so they are shared.</summary>
    public StoreFileModel Copy() => new()
    {
        SchemaVersion = SchemaVersion,
        Transitions = new Dictionary<string, TransitionSpec>(Transitions),
        PlaylistOverrides = PlaylistOverrides.ToDictionary(
            playlist => playlist.Key, playlist => new Dictionary<string, TransitionSpec>(playlist.Value)),
    };
}
