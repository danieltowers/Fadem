using Faderio.Core.Domain;

namespace Faderio.Core.Queue;

/// <summary>Playback loop modes.</summary>
public enum RepeatMode
{
    Off,
    All,
    One,
}

/// <summary>
/// The runtime playback sequence — real tracks only; nothing that is not a track ever
/// occupies a row (ADR 0001). Not a saved artifact: playlists load into it.
/// </summary>
public sealed class PlaybackQueue
{
    private readonly List<Track> _tracks = new();
    private readonly Random _rng;

    public PlaybackQueue(Random? rng = null) => _rng = rng ?? new Random();

    public IReadOnlyList<Track> Tracks => _tracks;
    public int Count => _tracks.Count;
    public int CurrentIndex { get; private set; } = -1;

    public Track? CurrentTrack =>
        CurrentIndex >= 0 && CurrentIndex < _tracks.Count ? _tracks[CurrentIndex] : null;

    public void SetTracks(IEnumerable<Track> tracks)
    {
        _tracks.Clear();
        _tracks.AddRange(tracks);
        CurrentIndex = _tracks.Count > 0 ? 0 : -1;
    }

    public void AddRange(IEnumerable<Track> tracks)
    {
        _tracks.AddRange(tracks);
        if (CurrentIndex < 0 && _tracks.Count > 0)
        {
            CurrentIndex = 0;
        }
    }

    public void Add(Track track) => AddRange(new[] { track });

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _tracks.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        _tracks.RemoveAt(index);
        if (_tracks.Count == 0)
        {
            CurrentIndex = -1;
        }
        else if (index < CurrentIndex)
        {
            CurrentIndex--;
        }
        else if (CurrentIndex >= _tracks.Count)
        {
            CurrentIndex = _tracks.Count - 1;
        }
    }

    public void Clear()
    {
        _tracks.Clear();
        CurrentIndex = -1;
    }

    /// <summary>
    /// Randomises the order of the tracks after the current one (Fisher–Yates). Transitions
    /// need no special handling: any authored pair that becomes adjacent gets its transition.
    /// </summary>
    public void ShuffleUpcoming()
    {
        for (var i = Math.Max(CurrentIndex + 1, 0); i < _tracks.Count; i++)
        {
            var j = _rng.Next(i, _tracks.Count);
            (_tracks[i], _tracks[j]) = (_tracks[j], _tracks[i]);
        }
    }

    /// <summary>The index of the track after <paramref name="index"/>, honouring repeat mode.
    /// The repeat-all wrap is a real adjacency: last → first. Null when playback would end.</summary>
    public int? IndexAfter(int index, RepeatMode repeat)
    {
        if (index < 0 || index >= _tracks.Count)
        {
            return null;
        }
        if (repeat == RepeatMode.One)
        {
            return index;
        }
        if (index + 1 < _tracks.Count)
        {
            return index + 1;
        }
        return repeat == RepeatMode.All ? 0 : null;
    }

    /// <summary>The index that plays next for the current track, or null when playback ends.</summary>
    public int? NextIndex(RepeatMode repeat) => IndexAfter(CurrentIndex, repeat);

    /// <summary>The previous track index, or null when at the start of the queue.</summary>
    public int? PreviousIndex() => CurrentIndex > 0 ? CurrentIndex - 1 : null;

    public void SetCurrent(int index)
    {
        if (index < 0 || index >= _tracks.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        CurrentIndex = index;
    }
}
