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

    /// <summary>Puts the same entries in a new order; the current entry stays current.</summary>
    public void Reorder(IEnumerable<QueueEntry> order)
    {
        var current = Current;
        SetEntries(order);
        if (current is not null && IndexOf(current.Id) is var index and >= 0)
        {
            CurrentIndex = index;
        }
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

    /// <summary>The index that follows <paramref name="index"/> at the track's natural end.
    /// Repeat-one plays the same row again; otherwise it is <see cref="IndexSkippedTo(int, RepeatMode)"/>.
    /// Null when playback would end.</summary>
    public int? IndexAfter(int index, RepeatMode repeat) =>
        repeat == RepeatMode.One && index >= 0 && index < _entries.Count ? index : IndexSkippedTo(index, repeat);

    /// <summary>The index ⏭ reaches from <paramref name="index"/>.</summary>
    public int? IndexSkippedTo(int index, RepeatMode repeat) => IndexSkippedTo(index, _entries.Count, repeat);

    /// <summary>The index ⏭ reaches from <paramref name="index"/> in a queue of
    /// <paramref name="count"/> rows: the next row, or under repeat-all the wrap to the first, a
    /// real adjacency. Repeat-one governs only a track's natural end, so a skip under it moves on
    /// too. Null past the last row with repeat off.</summary>
    public static int? IndexSkippedTo(int index, int count, RepeatMode repeat)
    {
        if (index < 0 || index >= count)
        {
            return null;
        }
        if (index + 1 < count)
        {
            return index + 1;
        }
        return repeat == RepeatMode.All ? 0 : null;
    }

    /// <summary>Each index from <paramref name="first"/>, then <paramref name="step"/> from it, once
    /// each: the walk ends when a step gives null or comes back round under repeat-all.</summary>
    public static IEnumerable<int> Walk(int? first, Func<int, int?> step)
    {
        var seen = new HashSet<int>();
        for (var index = first; index is { } i && seen.Add(i); index = step(i))
        {
            yield return i;
        }
    }

    /// <summary>The index ⏮ reaches from <paramref name="index"/>: the row before it, or at the
    /// head under repeat-all the last row. Null at the head otherwise.</summary>
    public int? IndexBefore(int index, RepeatMode repeat)
    {
        if (index < 0 || index >= _entries.Count)
        {
            return null;
        }
        if (index > 0)
        {
            return index - 1;
        }
        return repeat == RepeatMode.All ? _entries.Count - 1 : null;
    }

    public void SetCurrent(int index)
    {
        if (index < 0 || index >= _entries.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        CurrentIndex = index;
    }
}
