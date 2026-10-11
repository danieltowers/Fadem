using Fadem.Core.Domain;
using Fadem.Core.Playback;
using Fadem.Core.Transitions;

namespace Fadem.Core.Tests;

/// <summary>Build issue 05: <c>Move(rows, before)</c> through the seam. Rows keep their
/// identity, so a move never interrupts what plays; it only changes what comes next, and only
/// while the transition into it has not started.</summary>
public sealed class MoveRowsTests
{
    private const int Second = 44100;
    private const double FA = 1000;
    private const double FB = 2000;
    private const double FC = 3000;

    private static TransitionSpec CrossfadeAt1s(double duration) => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
        FadeInPoint = TimeSpan.Zero,
        FadeOutDuration = TimeSpan.FromSeconds(duration),
        FadeInDuration = TimeSpan.FromSeconds(duration),
    };

    private static string[] Paths(TestEngine tc) => tc.View.Queue.Select(r => r.Track.FilePath).ToArray();

    [Fact]
    public void MovingTwoRows_MovesThemTogether_InTheirOrder()
    {
        using var tc = TestEngine.Create();
        var p = Enumerable.Range(0, 5).Select(i => TestAudio.WriteDcWav($"m{i}.wav", 1, 0.1 * (i + 1))).ToArray();
        tc.Add(p);

        // Picked out of order; they land in the order the queue had them.
        tc.Engine.Move(new[] { tc.RowOf(p[3]), tc.RowOf(p[1]) }, before: tc.RowOf(p[0]));

        Assert.Equal(new[] { p[1], p[3], p[0], p[2], p[4] }, Paths(tc));
    }

    [Fact]
    public void MovingThePlayingRow_KeepsItPlaying_Uninterrupted()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 3, FA);
        var b = TestAudio.WriteToneWav("b.wav", 3, FB);
        var c = TestAudio.WriteToneWav("c.wav", 3, FC);
        tc.Play(a, b, c);
        tc.Output.Advance(Second / 2);
        var changes = tc.AttendedChanges.Count;

        tc.Engine.Move(new[] { tc.RowOf(a) }, before: null);

        Assert.Equal(new[] { b, c, a }, Paths(tc));
        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(0.5, tc.Playhead.TotalSeconds, 2);
        tc.Output.Advance(Second);
        Assert.Equal(changes, tc.AttendedChanges.Count);
        Assert.Equal(1.5, tc.Playhead.TotalSeconds, 2);
        Assert.Equal(0.4, tc.Rendered.ToneAmplitude(FA, Second), 2);
    }

    [Fact]
    public void MovingARowIntoTheNextPlace_BeforeTheFadeStarts_FiresItsAdjacency()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 3, FA);
        var b = TestAudio.WriteToneWav("b.wav", 3, FB);
        var c = TestAudio.WriteToneWav("c.wav", 3, FC);
        tc.Add(a, b, c);
        tc.Store.Upsert(a, c, CrossfadeAt1s(1.0)); // A → B is unauthored: a hard cut at A's end
        tc.Engine.Play();
        tc.Output.Advance(Second / 2);

        tc.Engine.Move(new[] { tc.RowOf(c) }, before: tc.RowOf(b));

        Assert.Equal(new[] { a, c, b }, Paths(tc));
        tc.Output.Advance(Second); // 1.5 s: halfway through A → C
        Assert.Equal(c, tc.View.Incoming!.Row.Track.FilePath);
        Assert.True(tc.Rendered.ToneAmplitude(FC, Second + Second / 2 - 2048) > 0.1, "C must be fading in");
        Assert.True(tc.Rendered.ToneAmplitude(FB, Second + Second / 2 - 2048) < 0.05, "B must not sound");
    }

    [Fact]
    public void TheSameMove_AfterTheFadeStarted_ChangesNothingAudible()
    {
        RenderedAudio Render(bool move)
        {
            using var tc = TestEngine.Create();
            var a = TestAudio.WriteToneWav("a.wav", 3, FA);
            var b = TestAudio.WriteToneWav("b.wav", 3, FB);
            var c = TestAudio.WriteToneWav("c.wav", 3, FC);
            tc.Add(a, b, c);
            tc.Store.Upsert(a, b, CrossfadeAt1s(1.0));
            tc.Store.Upsert(a, c, CrossfadeAt1s(0.5));
            tc.Engine.Play();
            tc.Output.Advance(Second + Second / 4); // 1.25 s: B is fading in under A
            if (move)
            {
                tc.Engine.Move(new[] { tc.RowOf(c) }, before: tc.RowOf(b));
                Assert.Equal(new[] { a, c, b }, Paths(tc));
                Assert.Equal(b, tc.View.Incoming!.Row.Track.FilePath);
            }
            tc.Output.Advance(Second + Second / 4); // to 2.5 s: the fade has played out
            Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
            return tc.Rendered;
        }

        var moved = Render(move: true);
        var unmoved = Render(move: false);

        Assert.Equal(unmoved.Frames, moved.Frames);
        for (long f = 0; f < moved.Frames; f++)
        {
            Assert.Equal(unmoved.Sample(f, 0), moved.Sample(f, 0));
        }
    }

    [Fact]
    public void AMove_WithShuffleOff_IsTheOrderThatShufflingOffReturnsTo()
    {
        using var tc = TestEngine.Create(rngSeed: 7);
        var p = Enumerable.Range(0, 5).Select(i => TestAudio.WriteDcWav($"o{i}.wav", 1, 0.1 * (i + 1))).ToArray();
        tc.Add(p);

        tc.Engine.Move(new[] { tc.RowOf(p[4]) }, before: tc.RowOf(p[1]));
        tc.Engine.SetShuffle(true);
        tc.Engine.SetShuffle(false);

        Assert.Equal(new[] { p[0], p[4], p[1], p[2], p[3] }, Paths(tc));
    }

    [Fact]
    public void AMove_UnderShuffle_ChangesTheShuffledOrder_AndShufflingOffReturnsToTheOriginal()
    {
        using var tc = TestEngine.Create(rngSeed: 7);
        var p = Enumerable.Range(0, 5).Select(i => TestAudio.WriteDcWav($"s{i}.wav", 1, 0.1 * (i + 1))).ToArray();
        tc.Add(p);
        tc.Engine.SetShuffle(true);
        var shuffled = Paths(tc);

        tc.Engine.Move(new[] { tc.RowOf(shuffled[4]) }, before: tc.RowOf(shuffled[1]));

        Assert.Equal(new[] { shuffled[0], shuffled[4], shuffled[1], shuffled[2], shuffled[3] }, Paths(tc));
        tc.Engine.SetShuffle(false);
        Assert.Equal(p, Paths(tc));
    }

    [Fact]
    public void DroppingRowsBeforeOneOfThemselves_LandsThemBeforeTheNextRowLeft()
    {
        using var tc = TestEngine.Create();
        var p = Enumerable.Range(0, 5).Select(i => TestAudio.WriteDcWav($"d{i}.wav", 1, 0.1 * (i + 1))).ToArray();
        tc.Add(p);

        // Rows 0 and 2 dropped at row 2: they close up there, before row 3.
        tc.Engine.Move(new[] { tc.RowOf(p[0]), tc.RowOf(p[2]) }, before: tc.RowOf(p[2]));

        Assert.Equal(new[] { p[1], p[0], p[2], p[3], p[4] }, Paths(tc));
        Assert.Empty(tc.Notices);
    }

    [Fact]
    public void MovingRowsThatAreGone_MovesTheRest_AndSaysSoOnce()
    {
        using var tc = TestEngine.Create();
        var p = Enumerable.Range(0, 3).Select(i => TestAudio.WriteDcWav($"g{i}.wav", 1, 0.1 * (i + 1))).ToArray();
        var rows = tc.Add(p);
        tc.Engine.Remove(new[] { rows[1].Id });

        tc.Engine.Move(new[] { rows[1].Id, rows[2].Id }, before: rows[0].Id);

        Assert.Equal(new[] { p[2], p[0] }, Paths(tc));
        Assert.Single(tc.Notices);
        Assert.Contains("no longer in the queue", tc.Notices[0]);
    }

    [Fact]
    public void DroppingBeforeARowThatIsGone_MovesNothing_AndSaysSo()
    {
        using var tc = TestEngine.Create();
        var p = Enumerable.Range(0, 3).Select(i => TestAudio.WriteDcWav($"n{i}.wav", 1, 0.1 * (i + 1))).ToArray();
        var rows = tc.Add(p);
        tc.Engine.Remove(new[] { rows[1].Id });
        var queueVersion = tc.View.QueueVersion;

        tc.Engine.Move(new[] { rows[2].Id }, before: rows[1].Id);

        Assert.Equal(new[] { p[0], p[2] }, Paths(tc));
        Assert.Equal(queueVersion, tc.View.QueueVersion);
        Assert.Single(tc.Notices);
        Assert.Contains("no longer in the queue", tc.Notices[0]);
    }
}
