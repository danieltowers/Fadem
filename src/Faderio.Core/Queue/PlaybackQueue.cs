using Faderio.Core.Domain;
using Faderio.Core.Playback;

namespace Faderio.Core.Queue;

/// <summary>Playback loop modes.</summary>
public enum RepeatMode
{
    Off,
    All,
    One,
}

/// <summary>A track in the queue under its engine-issued row identity.</summary>
internal sealed record QueueEntry(RowId Id, Track Track);

/// <summary>
/// The runtime playback sequence — real tracks only; nothing that is not a track ever
/// occupies a row (ADR 0004). Not a saved artifact: playlists load into it. Engine-internal
/// (ADR 0003): the shell sees an immutable list of rows, never this.
/// </summary>
internal sealed class PlaybackQueue
{
    private readonly List<QueueEntry> _entries = new();
    private readonly Random _rng;

    public PlaybackQueue(Random? rng = null) => _rng = rng ?? new Random();

    public IReadOnlyList<QueueEntry> Entries => _entries;
    public int Count => _entries.Count;
    public int CurrentIndex { get; private set; } = -1;

    public QueueEntry? Current =>
        CurrentIndex >= 0 && CurrentIndex < _entries.Count ? _entries[CurrentIndex] : null;

    public int IndexOf(RowId id) => _entries.FindIndex(e => e.Id == id);

    public void SetEntries(IEnumerable<QueueEntry> entries)
    {
        _entries.Clear();
        _entries.AddRange(entries);
        CurrentIndex = _entries.Count > 0 ? 0 : -1;
    }

    public void Add(QueueEntry entry)
    {
        _entries.Add(entry);
        if (CurrentIndex < 0)
        {
            CurrentIndex = 0;
        }
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _entries.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        _entries.RemoveAt(index);
        if (_entries.Count == 0)
        {
            CurrentIndex = -1;
        }
        else if (index < CurrentIndex)
        {
            CurrentIndex--;
        }
        else if (CurrentIndex >= _entries.Count)
        {
            CurrentIndex = _entries.Count - 1;
        }
    }

    public void Clear()
    {
        _entries.Clear();
        CurrentIndex = -1;
    }

    /// <summary>
    /// Randomises the order of the tracks after the current one (Fisher–Yates). Transitions
    /// need no special handling: any authored pair that becomes adjacent gets its transition.
    /// </summary>
    public void ShuffleUpcoming()
    {
        for (var i = Math.Max(CurrentIndex + 1, 0); i < _entries.Count; i++)
        {
            var j = _rng.Next(i, _entries.Count);
            (_entries[i], _entries[j]) = (_entries[j], _entries[i]);
        }
    }

    /// <summary>The index of the track after <paramref name="index"/>, honouring repeat mode.
    /// The repeat-all wrap is a real adjacency: last → first. Null when playback would end.</summary>
    public int? IndexAfter(int index, RepeatMode repeat)
    {
        if (index < 0 || index >= _entries.Count)
        {
            return null;
        }
        if (repeat == RepeatMode.One)
        {
            return index;
        }
        if (index + 1 < _entries.Count)
        {
            return index + 1;
        }
        return repeat == RepeatMode.All ? 0 : null;
    }

    /// <summary>The previous track index, or null when at the start of the queue.</summary>
    public int? PreviousIndex() => CurrentIndex > 0 ? CurrentIndex - 1 : null;

    public void SetCurrent(int index)
    {
        if (index < 0 || index >= _entries.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        CurrentIndex = index;
    }
}
