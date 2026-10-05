using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Queue;

namespace Faderio.Core.Tests;

/// <summary>The fade marks on the attended voice: where its fade-in and fade-out begin and end,
/// as the transitions into and out of its row resolve, for the seek bar to draw.</summary>
public sealed class FadeMarksTests
{
    private const int Second = 44100;

    private static TransitionSpec Fade(double fadeOutPoint, double fadeOut, double fadeInPoint, double fadeIn) => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(fadeOutPoint), FromEnd: false),
        FadeOutDuration = TimeSpan.FromSeconds(fadeOut),
        FadeInPoint = new FadePointSpec(TimeSpan.FromSeconds(fadeInPoint), FromEnd: false),
        FadeInDuration = TimeSpan.FromSeconds(fadeIn),
        Curve = FadeCurveKind.Linear,
    };

    private static double? Seconds(Point? point) => point?.TotalSeconds;

    [Fact]
    public void TheMarks_AreTheFadesIntoAndOutOfTheAttendedRow()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 12, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 12, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 12, 0.3);
        tc.Add(a, b, c);
        tc.Store.Upsert(a, b, Fade(8, 2, fadeInPoint: 1, fadeIn: 3));
        tc.Store.Upsert(b, c, Fade(9, 4, fadeInPoint: 0, fadeIn: 2));

        tc.Engine.Play();
        var marks = tc.View.Attended!.Marks;
        Assert.Equal((null, null), (Seconds(marks.FadeInStart), Seconds(marks.FadeInEnd))); // the first row
        Assert.Equal((8.0, 10.0), (Seconds(marks.FadeOutStart)!.Value, Seconds(marks.FadeOutEnd)!.Value));

        tc.Engine.PlayRow(tc.RowOf(b));
        marks = tc.View.Attended!.Marks;
        Assert.Equal((1.0, 4.0), (Seconds(marks.FadeInStart)!.Value, Seconds(marks.FadeInEnd)!.Value));
        // 9 s + 4 s passes the 12 s track: the fade ends at its end.
        Assert.Equal((9.0, 12.0), (Seconds(marks.FadeOutStart)!.Value, Seconds(marks.FadeOutEnd)!.Value));
    }

    [Fact]
    public void EachSide_CarriesItsTransitionsCurve_EqualPowerWhereItHasNone()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 12, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 12, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 12, 0.3);
        tc.Add(a, b, c);
        tc.Store.Upsert(a, b, Fade(8, 2, fadeInPoint: 1, fadeIn: 3) with { Curve = FadeCurveKind.SCurve });
        tc.Store.Upsert(b, c, Fade(9, 2, fadeInPoint: 0, fadeIn: 2) with { Curve = null });

        tc.Engine.PlayRow(tc.RowOf(b));
        var marks = tc.View.Attended!.Marks;
        Assert.Equal(FadeCurveKind.SCurve, marks.FadeInCurve);     // from a → b
        Assert.Equal(FadeCurveKind.EqualPower, marks.FadeOutCurve); // b → c names none

        tc.Store.Upsert(b, c, Fade(9, 2, fadeInPoint: 0, fadeIn: 2)); // linear
        tc.Engine.PlayRow(tc.RowOf(b));
        Assert.Equal(FadeCurveKind.Linear, tc.View.Attended!.Marks.FadeOutCurve);
    }

    [Fact]
    public void AHardCut_HasNoMarks_AndNorDoesStopped()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 6, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 6, 0.2);
        tc.Add(a, b);
        Assert.Null(tc.View.Attended);

        tc.Engine.PlayRow(tc.RowOf(b));
        Assert.Equal(FadeMarks.None, tc.View.Attended!.Marks);
        tc.Engine.SetRepeat(RepeatMode.All); // b → a, unauthored: a hard cut
        tc.Engine.PlayRow(tc.RowOf(a));
        Assert.Equal(FadeMarks.None, tc.View.Attended!.Marks);
    }

    [Fact]
    public void AnAuthoringChange_MovesTheMarks()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 12, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 12, 0.2);
        tc.Add(a, b);
        tc.Engine.Play();
        tc.Output.Advance(Second);
        Assert.Null(tc.View.Attended!.Marks.FadeOutStart);

        tc.Engine.Author(new Adjacency(tc.View.Queue[0].Track, tc.View.Queue[1].Track), Fade(7, 1, 0, 1));

        Assert.Equal(7.0, Seconds(tc.View.Attended!.Marks.FadeOutStart));
    }
}
