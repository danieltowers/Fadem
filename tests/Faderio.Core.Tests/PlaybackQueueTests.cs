using Faderio.Core.Domain;
using Faderio.Core.Queue;

namespace Faderio.Core.Tests;

public sealed class PlaybackQueueTests
{
    private static Track T(string path) => new() { FilePath = path };

    [Fact]
    public void SetTracks_ResetsCurrentToFirst()
    {
        var queue = new PlaybackQueue();
        queue.SetTracks(new[] { T("a"), T("b"), T("c") });
        Assert.Equal(3, queue.Count);
        Assert.Equal(0, queue.CurrentIndex);
        Assert.Equal("a", queue.CurrentTrack!.FilePath);
    }

    [Fact]
    public void NextIndex_FollowsOrder()
    {
        var queue = new PlaybackQueue();
        queue.SetTracks(new[] { T("a"), T("b"), T("c") });
        Assert.Equal(1, queue.NextIndex(RepeatMode.Off));
        queue.SetCurrent(2);
        Assert.Null(queue.NextIndex(RepeatMode.Off));
    }

    [Fact]
    public void RepeatAll_WrapsToFirst_AsARealAdjacency()
    {
        var queue = new PlaybackQueue();
        queue.SetTracks(new[] { T("a"), T("b"), T("c") });
        queue.SetCurrent(2);
        Assert.Equal(0, queue.IndexAfter(2, RepeatMode.All));
    }

    [Fact]
    public void RepeatOne_RepeatsTheSameTrack()
    {
        var queue = new PlaybackQueue();
        queue.SetTracks(new[] { T("a"), T("b") });
        Assert.Equal(1, queue.IndexAfter(1, RepeatMode.One));
    }

    [Fact]
    public void PreviousIndex_StopsAtTheStart()
    {
        var queue = new PlaybackQueue();
        queue.SetTracks(new[] { T("a"), T("b") });
        Assert.Null(queue.PreviousIndex());
        queue.SetCurrent(1);
        Assert.Equal(0, queue.PreviousIndex());
    }

    [Fact]
    public void RemoveAt_AdjustsCurrent()
    {
        var queue = new PlaybackQueue();
        queue.SetTracks(new[] { T("a"), T("b"), T("c") });
        queue.SetCurrent(2);
        queue.RemoveAt(0);
        Assert.Equal(1, queue.CurrentIndex);
        Assert.Equal("c", queue.CurrentTrack!.FilePath);
        queue.RemoveAt(1);
        Assert.Equal(0, queue.CurrentIndex);
    }

    [Fact]
    public void ShuffleUpcoming_IsDeterministicWithASeed_AndKeepsTheCurrentTrack()
    {
        var queue = new PlaybackQueue(new Random(1234));
        queue.SetTracks(new[] { T("a"), T("b"), T("c"), T("d"), T("e") });
        queue.SetCurrent(2);

        queue.ShuffleUpcoming();

        Assert.Equal("c", queue.CurrentTrack!.FilePath);
        // Deterministic for the same seed.
        var queue2 = new PlaybackQueue(new Random(1234));
        queue2.SetTracks(new[] { T("a"), T("b"), T("c"), T("d"), T("e") });
        queue2.SetCurrent(2);
        queue2.ShuffleUpcoming();
        Assert.Equal(
            queue.Tracks.Select(t => t.FilePath).ToList(),
            queue2.Tracks.Select(t => t.FilePath).ToList());
        // And it is a genuine permutation of the same tracks.
        Assert.Equal(
            new[] { "a", "b", "c", "d", "e" }.OrderBy(x => x),
            queue.Tracks.Select(t => t.FilePath).OrderBy(x => x));
    }

    [Fact]
    public void M3U_SaveLoad_RoundTripsAQueueOfTracks()
    {
        var dir = Path.Combine(Path.GetTempPath(), "faderio-m3u", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var tracks = new List<Track>
            {
                new() { FilePath = Path.Combine(dir, "one.wav"), Title = "One", Artist = "Artist A", Duration = TimeSpan.FromSeconds(100.4) },
                new() { FilePath = Path.Combine(dir, "two.wav"), Title = "Two", Duration = TimeSpan.FromSeconds(12) },
            };
            var playlistPath = Path.Combine(dir, "queue.m3u");
            M3UPlaylist.Save(playlistPath, tracks);

            var loaded = M3UPlaylist.Load(playlistPath);
            Assert.Equal(2, loaded.Count);
            Assert.Equal(tracks[0].FilePath, loaded[0]);
            Assert.Equal(tracks[1].FilePath, loaded[1]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void M3U_HasTheExtm3uHeader()
    {
        var dir = Path.Combine(Path.GetTempPath(), "faderio-m3u", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var playlistPath = Path.Combine(dir, "q.m3u");
            M3UPlaylist.Save(playlistPath, new List<Track> { T("x.wav") });
            Assert.StartsWith("#EXTM3U", File.ReadAllText(playlistPath));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
