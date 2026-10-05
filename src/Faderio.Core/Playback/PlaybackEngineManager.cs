using Faderio.Core.Domain;
using Faderio.Core.Transitions;

namespace Faderio.Core.Playback;

public sealed partial class PlaybackEngine
{
    // ── The transition manager's queries (playlists issue 05, D24) ───────────────────────

    /// <summary>The store was written since the last snapshot, for <see cref="StoreVersionNow"/>.</summary>
    private bool _storeWritten;

    /// <summary>The store and the playlist <see cref="StoreVersionNow"/> last saw.</summary>
    private (ITransitionStore? Store, string? Playlist) _storeSeen;
    private long _storeVersion;

    /// <summary><see cref="PlayerView.StoreVersion"/>: moved on by a write to the store, and by
    /// another store or another playlist since the last snapshot, whichever command changed them.</summary>
    private long StoreVersionNow()
    {
        if (_storeWritten || _storeSeen != (_store, _openPlaylist))
        {
            _storeSeen = (_store, _openPlaylist);
            _storeVersion++;
            _storeWritten = false;
        }
        return _storeVersion;
    }

    /// <summary>Every adjacency in the open library's store that <paramref name="track"/> is in,
    /// outgoing or incoming, with its global transition, the open playlist's override of it and
    /// the two merged, as <see cref="Transition"/> gives them. The store holds an adjacency when it
    /// has a global transition for it or the open playlist overrides it; another playlist's
    /// override is that playlist's business. Outgoing first, then incoming, each by the other
    /// track's title. Reads the store and probes the other tracks' files; nothing is decoded.</summary>
    public IReadOnlyList<StoredAdjacency> AdjacenciesInStore(Track track)
    {
        ThrowIfDisposed();
        var path = Path.GetFullPath(track.FilePath);
        var pairs = Run(() => StoredPairs().Where(p => p.Outgoing == path || p.Incoming == path).ToList());
        var tracks = new StoreTracks(View.Queue.Select(r => r.Track).Prepend(track), ResolveFromDisk);
        return pairs
            .Select(p => new StoredAdjacency(
                new Adjacency(tracks.Of(p.Outgoing), tracks.Of(p.Incoming)), IsOutgoing: p.Outgoing == path, p.Layers))
            .OrderBy(a => a.IsOutgoing ? 0 : 1)
            .ThenBy(a => a.Other.ToString(), StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Every track the open library's store names, in the adjacencies
    /// <see cref="AdjacenciesInStore"/> lists, by title. A queued track is the queue's; any other is
    /// probed as an add probes it, for its title and artist. One whose file cannot be probed, gone
    /// or no longer promised, is its path alone and <see cref="Track.Gone"/>. Nothing is decoded.</summary>
    public IReadOnlyList<Track> TracksInStore()
    {
        ThrowIfDisposed();
        var paths = Run(() => StoredPairs().SelectMany(p => new[] { p.Outgoing, p.Incoming }).Distinct().ToList());
        var tracks = new StoreTracks(View.Queue.Select(r => r.Track), ResolveFromDisk);
        return paths
            .Select(tracks.Of)
            .OrderBy(t => t.ToString(), StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>The pairs the manager lists, by the store's full paths, each with its layers:
    /// every pair with a global transition, and every pair the open playlist overrides. A blank
    /// global left by an older build, or an override that sets nothing, is nothing. Under the lock.</summary>
    private List<(string Outgoing, string Incoming, TransitionLayers Layers)> StoredPairs()
    {
        if (_store is not { } store)
        {
            return [];
        }
        var overridden = _openPlaylist is { } name ? store.OverridePairs(name) : [];
        return store.Pairs()
            .Concat(overridden)
            .Distinct()
            .Select(p =>
            {
                var global = store.Get(p.Outgoing, p.Incoming);
                var playlistOverride = OverrideOf(p.Outgoing, p.Incoming) is { IsEmpty: false } o ? o : null;
                return (p.Outgoing, p.Incoming, Layers: new TransitionLayers(
                    global, playlistOverride, TransitionResolver.Merge(global, playlistOverride))
                {
                    BlankDuration = _resolver.BlankDuration,
                });
            })
            .Where(p => p.Layers.Global is { IsBlank: false } || p.Layers.Override is not null)
            .ToList();
    }

    /// <summary>The tracks the store's paths name, each found once: the queue's own track for a
    /// path it holds, else a probe of the file. Outside the lock, since a probe reads the disk.</summary>
    private sealed class StoreTracks
    {
        private readonly Dictionary<string, Track> _found = new();
        private readonly Func<string, (Track? Track, Rejection? Rejection)> _resolve;

        public StoreTracks(IEnumerable<Track> known, Func<string, (Track? Track, Rejection? Rejection)> resolve)
        {
            _resolve = resolve;
            foreach (var track in known)
            {
                _found.TryAdd(Path.GetFullPath(track.FilePath), track);
            }
        }

        public Track Of(string path)
        {
            if (!_found.TryGetValue(path, out var track))
            {
                track = _resolve(path).Track ?? new Track { FilePath = path, Gone = true };
                _found[path] = track;
            }
            return track;
        }
    }
}
