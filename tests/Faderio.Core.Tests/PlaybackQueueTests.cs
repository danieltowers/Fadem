using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Queue;

namespace Faderio.Core.Tests;

public sealed class PlaybackQueueTests
{
    private static long _nextId;

    private static QueueEntry E(string path) =>
        new(new RowId(Interlocked.Increment(ref _nextId)), new Track { FilePath = path });

    private static IEnumerable<string> Paths(PlaybackQueue queue) => queue.Entries.Select(e => e.Track.FilePath);

    [Fact]
    public void SetEntries_ResetsCurrentToFirst()
    {
        var queue = new PlaybackQueue();
        queue.SetEntries(new[] { E("a"), E("b"), E("c") });
        Assert.Equal(3, queue.Count);
        Assert.Equal(0, queue.CurrentIndex);
        Assert.Equal("a", queue.Current!.Track.FilePath);
    }

    [Fact]
    public void IndexAfter_FollowsOrder()
    {
        var queue = new PlaybackQueue();
        queue.SetEntries(new[] { E("a"), E("b"), E("c") });
        Assert.Equal(1, queue.IndexAfter(0, RepeatMode.Off));
        Assert.Null(queue.IndexAfter(2, RepeatMode.Off));
    }

    [Fact]
    public void RepeatAll_WrapsToFirst_AsARealAdjacency()
    {
        var queue = new PlaybackQueue();
        queue.SetEntries(new[] { E("a"), E("b"), E("c") });
        Assert.Equal(0, queue.IndexAfter(2, RepeatMode.All));
    }

    [Fact]
    public void RepeatOne_RepeatsTheSameTrack()
    {
        var queue = new PlaybackQueue();
        queue.SetEntries(new[] { E("a"), E("b") });
        Assert.Equal(1, queue.IndexAfter(1, RepeatMode.One));
    }

    [Fact]
    public void PreviousIndex_StopsAtTheStart()
    {
        var queue = new PlaybackQueue();
        queue.SetEntries(new[] { E("a"), E("b") });
        Assert.Null(queue.PreviousIndex());
        queue.SetCurrent(1);
        Assert.Equal(0, queue.PreviousIndex());
    }

    [Fact]
    public void RemoveAt_AdjustsCurrent()
    {
        var queue = new PlaybackQueue();
        queue.SetEntries(new[] { E("a"), E("b"), E("c") });
        queue.SetCurrent(2);
        queue.RemoveAt(0);
        Assert.Equal(1, queue.CurrentIndex);
        Assert.Equal("c", queue.Current!.Track.FilePath);
        queue.RemoveAt(1);
        Assert.Equal(0, queue.CurrentIndex);
    }

    [Fact]
    public void RowIds_SurviveAShuffle()
    {
        var queue = new PlaybackQueue(new Random(7));
        var entries = new[] { E("a"), E("b"), E("c"), E("d"), E("e") };
        queue.SetEntries(entries);
        queue.ShuffleUpcoming();

        foreach (var entry in entries)
        {
            var index = queue.IndexOf(entry.Id);
            Assert.True(index >= 0);
            Assert.Equal(entry.Track.FilePath, queue.Entries[index].Track.FilePath);
        }
    }

    [Fact]
    public void ShuffleUpcoming_IsDeterministicWithASeed_AndKeepsTheCurrentTrack()
    {
        var queue = new PlaybackQueue(new Random(1234));
        queue.SetEntries(new[] { E("a"), E("b"), E("c"), E("d"), E("e") });
        queue.SetCurrent(2);

        queue.ShuffleUpcoming();

        Assert.Equal("c", queue.Current!.Track.FilePath);
        // Deterministic for the same seed.
        var queue2 = new PlaybackQueue(new Random(1234));
        queue2.SetEntries(new[] { E("a"), E("b"), E("c"), E("d"), E("e") });
        queue2.SetCurrent(2);
        queue2.ShuffleUpcoming();
        Assert.Equal(Paths(queue).ToList(), Paths(queue2).ToList());
        // And it is a genuine permutation of the same tracks.
        Assert.Equal(new[] { "a", "b", "c", "d", "e" }.OrderBy(x => x), Paths(queue).OrderBy(x => x));
    }
}
