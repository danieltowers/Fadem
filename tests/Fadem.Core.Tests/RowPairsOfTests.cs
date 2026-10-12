using Fadem.Core.Domain;
using Fadem.Core.Playback;
using Fadem.Core.Queue;

namespace Fadem.Core.Tests;

/// <summary>Where an adjacency is in the queue (seam markers issue 01): the queue marks the
/// editor's open pair from the rows the engine names, so the shell never matches tracks to rows.
/// <see cref="PlaybackEngine.RowPairsOf"/> goes by <see cref="PlaybackEngine.AdjacenciesOf"/>'s
/// rules, and gives every match in the order the queue reaches them.</summary>
public sealed class RowPairsOfTests
{
    private const int Second = 44100;

    /// <summary>The given files, 2 s each, queued in order; their rows.</summary>
    private static (string[] Files, IReadOnlyList<QueueRow> Rows) Queued(TestEngine tc, params string[] names)
    {
        var files = names.Distinct().ToDictionary(n => n, n => TestAudio.WriteDcWav($"pairs-{n}.wav", 2, 0.2));
        var paths = names.Select(n => files[n]).ToArray();
        tc.Add(paths);
        return (paths, tc.View.Queue);
    }

    [Fact]
    public void APairInTheQueue_IsItsTwoRows()
    {
        using var tc = TestEngine.Create();
        var (f, rows) = Queued(tc, "a", "b", "c");

        Assert.Equal(new[] { new RowPair(rows[1].Id, rows[2].Id) }, tc.Engine.RowPairsOf(tc.AdjacencyOf(f[1], f[2])));
    }

    [Fact]
    public void TwoTracksNotNeighbours_AreNoPair_UntilTheRowBetweenGoes()
    {
        using var tc = TestEngine.Create();
        var (f, rows) = Queued(tc, "a", "b", "c");
        var ab = tc.AdjacencyOf(f[0], f[1]);
        var ac = tc.AdjacencyOf(f[0], f[2]);
        Assert.Empty(tc.Engine.RowPairsOf(ac));

        tc.Engine.Remove(new[] { rows[1].Id });

        Assert.Empty(tc.Engine.RowPairsOf(ab));
        Assert.Equal(new[] { new RowPair(rows[0].Id, rows[2].Id) }, tc.Engine.RowPairsOf(ac));
    }

    [Fact]
    public void ThePairTwice_ComesInTheOrderTheQueueReachesIt()
    {
        using var tc = TestEngine.Create();
        var (f, rows) = Queued(tc, "a", "b", "c", "a", "b");
        var ab = tc.AdjacencyOf(f[0], f[1]);
        var first = new RowPair(rows[0].Id, rows[1].Id);
        var second = new RowPair(rows[3].Id, rows[4].Id);
        Assert.Equal(new[] { first, second }, tc.Engine.RowPairsOf(ab)); // nothing current: from the top

        tc.Engine.PlayRow(rows[2].Id);
        tc.Output.Advance(Second / 10);

        Assert.Equal(new[] { second, first }, tc.Engine.RowPairsOf(ab));
    }

    [Fact]
    public void UnderRepeatAll_TheLastRowGoesIntoTheFirst()
    {
        using var tc = TestEngine.Create();
        var (f, rows) = Queued(tc, "a", "b", "c");
        var ca = tc.AdjacencyOf(f[2], f[0]);
        Assert.Empty(tc.Engine.RowPairsOf(ca));

        tc.Engine.SetRepeat(RepeatMode.All);

        Assert.Equal(new[] { new RowPair(rows[2].Id, rows[0].Id) }, tc.Engine.RowPairsOf(ca));
    }

    [Fact]
    public void UnderRepeatOne_ARowGoesIntoItself_AndStillIntoTheNext()
    {
        using var tc = TestEngine.Create();
        var (f, rows) = Queued(tc, "a", "b");
        var aa = tc.AdjacencyOf(f[0], f[0]);
        Assert.Empty(tc.Engine.RowPairsOf(aa));

        tc.Engine.SetRepeat(RepeatMode.One);

        Assert.Equal(new[] { new RowPair(rows[0].Id, rows[0].Id) }, tc.Engine.RowPairsOf(aa));
        Assert.Equal(new[] { new RowPair(rows[0].Id, rows[1].Id) }, tc.Engine.RowPairsOf(tc.AdjacencyOf(f[0], f[1])));
    }

    [Fact]
    public void ARowMarkedUnreadable_IsPassedOver_AsTheEditorPassesOverIt()
    {
        using var tc = TestEngine.Create();
        var (f, rows) = Queued(tc, "a", "b", "c");
        var ab = tc.AdjacencyOf(f[0], f[1]);
        var ac = tc.AdjacencyOf(f[0], f[2]);
        File.Delete(f[1]);
        tc.Engine.Play();
        tc.Output.Advance(Second / 4); // A's boundary tried B and marked it
        Assert.True(tc.View.Queue[1].IsUnreadable);

        Assert.Empty(tc.Engine.RowPairsOf(ab));
        Assert.Equal(new[] { new RowPair(rows[0].Id, rows[2].Id) }, tc.Engine.RowPairsOf(ac));
    }
}
