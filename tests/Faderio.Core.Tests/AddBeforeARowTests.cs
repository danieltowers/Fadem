using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Transitions;

namespace Faderio.Core.Tests;

/// <summary>Phase 6 issue 03: <c>Add(paths, before)</c>, files dropped on the queue landing where
/// they were dropped. A job's rows land together in its order before the row it was given, as
/// <c>Move</c> places rows; with that row gone before the first lands, at the end.</summary>
public sealed class AddBeforeARowTests
{
    private const int Second = 44100;
    private const double FA = 1000;
    private const double FB = 2000;
    private const double FC = 3000;

    private static string[] Paths(TestEngine tc) => tc.View.Queue.Select(r => r.Track.FilePath).ToArray();

    private static string[] Tones(string prefix, int count) =>
        Enumerable.Range(0, count).Select(i => TestAudio.WriteDcWav($"{prefix}{i}.wav", 1, 0.1 * (i + 1))).ToArray();

    [Fact]
    public void FilesAddedBeforeARow_LandThere_InTheirOrder()
    {
        using var tc = TestEngine.Create();
        var q = Tones("q", 3);
        var rows = tc.Add(q);
        var n = Tones("n", 2);

        var report = tc.Engine.Add(n, before: rows[1].Id).Result;

        Assert.Equal(new[] { q[0], n[0], n[1], q[1], q[2] }, Paths(tc));
        Assert.Equal(n, report.Added.Select(r => r.Track.FilePath));
        Assert.Empty(tc.Notices);
    }

    [Fact]
    public void WithNoRow_TheyGoToTheEnd()
    {
        using var tc = TestEngine.Create();
        var q = Tones("e", 2);
        tc.Add(q);
        var n = Tones("f", 1);

        tc.Engine.Add(n, before: null).Wait();

        Assert.Equal(new[] { q[0], q[1], n[0] }, Paths(tc));
    }

    [Fact]
    public void AFolderAddedBeforeARow_KeepsItsOrder_InPlace()
    {
        using var tc = TestEngine.Create();
        var q = Tones("r", 2);
        var rows = tc.Add(q);
        var folder = Path.Combine(tc.TempDir, "Album");
        Directory.CreateDirectory(folder);
        foreach (var (name, i) in new[] { "1 - One.wav", "2 - Two.wav", "3 - Three.wav" }.Select((x, i) => (x, i)))
        {
            File.Copy(TestAudio.WriteDcWav($"album{i}.wav", 1, 0.2), Path.Combine(folder, name));
        }

        tc.Engine.Add(new[] { folder }, before: rows[1].Id).Wait();

        Assert.Equal(
            new[] { q[0], Path.Combine(folder, "1 - One.wav"), Path.Combine(folder, "2 - Two.wav"), Path.Combine(folder, "3 - Three.wav"), q[1] },
            Paths(tc));
    }

    [Fact]
    public void TwoAdds_WithDifferentRows_EachLandAtTheirOwn()
    {
        using var tc = TestEngine.Create(backgroundAdd: true);
        var q = Tones("t", 3);
        tc.Engine.Add(q).Wait();
        var rows = tc.View.Queue;
        var first = Tones("u", 2);
        var second = Tones("v", 2);

        var one = tc.Engine.Add(first, before: rows[2].Id);
        var two = tc.Engine.Add(second, before: rows[0].Id);
        Task.WaitAll(one, two);

        Assert.Equal(new[] { second[0], second[1], q[0], q[1], first[0], first[1], q[2] }, Paths(tc));
    }

    [Fact]
    public void TheRowRemovedBeforeTheFirstLands_SendsTheAddToTheEnd()
    {
        using var tc = TestEngine.Create();
        var q = Tones("w", 3);
        var rows = tc.Add(q);
        var n = Tones("x", 2);
        var probe = tc.Engine.ResolvePath;
        tc.Engine.ResolvePath = path =>
        {
            if (path == n[0])
            {
                tc.Engine.Remove(new[] { rows[1].Id }); // between the drop and its first row
            }
            return probe(path);
        };

        tc.Engine.Add(n, before: rows[1].Id).Wait();

        Assert.Equal(new[] { q[0], q[2], n[0], n[1] }, Paths(tc));
        Assert.Empty(tc.Notices);
    }

    [Fact]
    public void TheRowRemovedAfterTheFirstLands_KeepsTheRestTogether()
    {
        using var tc = TestEngine.Create();
        var q = Tones("y", 3);
        var rows = tc.Add(q);
        var n = Tones("z", 3);
        var probe = tc.Engine.ResolvePath;
        tc.Engine.ResolvePath = path =>
        {
            if (path == n[1])
            {
                tc.Engine.Remove(new[] { rows[1].Id });
            }
            return probe(path);
        };

        tc.Engine.Add(n, before: rows[1].Id).Wait();

        Assert.Equal(new[] { q[0], n[0], n[1], n[2], q[2] }, Paths(tc));
        Assert.Empty(tc.Notices);
    }

    [Fact]
    public void UnderShuffle_TheyLandAtTheRowInTheVisibleOrder_AndShufflingOffPutsThemAtTheEnd()
    {
        using var tc = TestEngine.Create(rngSeed: 7);
        var q = Tones("s", 4);
        tc.Add(q);
        tc.Engine.SetShuffle(true);
        var shuffled = Paths(tc);
        var n = Tones("sn", 2);

        tc.Engine.Add(n, before: tc.RowOf(shuffled[2])).Wait();

        Assert.Equal(new[] { shuffled[0], shuffled[1], n[0], n[1], shuffled[2], shuffled[3] }, Paths(tc));
        tc.Engine.SetShuffle(false);
        Assert.Equal(q.Concat(n), Paths(tc));
    }

    [Fact]
    public void WithShuffleOff_ThePlaceIsTheOrderThatShufflingOffReturnsTo()
    {
        using var tc = TestEngine.Create(rngSeed: 7);
        var q = Tones("o", 3);
        var rows = tc.Add(q);
        var n = Tones("on", 1);

        tc.Engine.Add(n, before: rows[1].Id).Wait();
        tc.Engine.SetShuffle(true);
        tc.Engine.SetShuffle(false);

        Assert.Equal(new[] { q[0], n[0], q[1], q[2] }, Paths(tc));
    }

    [Fact]
    public void AddedBeforeTheNextRow_BeforeTheFadeStarts_TheNewRowIsNext()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 3, FA);
        var b = TestAudio.WriteToneWav("b.wav", 3, FB);
        var rows = tc.Add(a, b);
        var c = TestAudio.WriteToneWav("c.wav", 3, FC);
        tc.Store.Upsert(a, c, CrossfadeAt1s(1.0)); // A → B is unauthored: a hard cut at A's end
        tc.Engine.Play();
        tc.Output.Advance(Second / 2);

        tc.Engine.Add(new[] { c }, before: rows[1].Id).Wait();

        Assert.Equal(new[] { a, c, b }, Paths(tc));
        tc.Output.Advance(Second); // 1.5 s: halfway through A → C
        Assert.Equal(c, tc.View.Incoming!.Row.Track.FilePath);
        Assert.True(tc.Rendered.ToneAmplitude(FC, Second + Second / 2 - 2048) > 0.1, "C must be fading in");
    }

    [Fact]
    public void AddedBeforeTheIncomingRow_AfterTheFadeStarted_TheFadePlaysOut()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 3, FA);
        var b = TestAudio.WriteToneWav("b.wav", 3, FB);
        var rows = tc.Add(a, b);
        tc.Store.Upsert(a, b, CrossfadeAt1s(1.0));
        tc.Engine.Play();
        tc.Output.Advance(Second + Second / 4); // 1.25 s: B is fading in under A
        var c = TestAudio.WriteToneWav("c.wav", 3, FC);

        tc.Engine.Add(new[] { c }, before: rows[1].Id).Wait();

        Assert.Equal(new[] { a, c, b }, Paths(tc));
        Assert.Equal(b, tc.View.Incoming!.Row.Track.FilePath);
        tc.Output.Advance(Second + Second / 4);
        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
    }

    private static TransitionSpec CrossfadeAt1s(double duration) => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
        FadeInPoint = TimeSpan.Zero,
        FadeOutDuration = TimeSpan.FromSeconds(duration),
        FadeInDuration = TimeSpan.FromSeconds(duration),
    };
}
