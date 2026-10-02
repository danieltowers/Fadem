using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Transitions;

namespace Faderio.Core.Tests;

/// <summary>
/// Build issue 14, the engine's half of the editor: converting a fade-out point to and from the
/// end, capturing into a from-end field, refusing impossible values with their reasons, and a
/// blank save removing the transition. Through the seam, on the fake output.
/// </summary>
public sealed class EditorAuthoringTests
{
    private const int Rate = 44100;
    private const int Latency = 4410;

    private static TransitionSpec Crossfade(FadePointSpec fadeOutPoint, FadeCurveKind? curve = null) => new()
    {
        FadeOutPoint = fadeOutPoint,
        FadeOutDuration = TimeSpan.FromSeconds(1),
        FadeInPoint = TimeSpan.Zero,
        FadeInDuration = TimeSpan.FromSeconds(1),
        Curve = curve,
    };

    private static TimeSpan Seconds(double s) => TimeSpan.FromSeconds(s);

    // ── From-end ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FromOtherEnd_On3m40_Turns3m12Into0m28_AndBack()
    {
        using var tc = TestEngine.Create();
        var track = tc.TrackOfLength(@"C:\music\outro.flac", 220);

        var fromEnd = tc.Engine.FromOtherEnd(track, Point.FromSeconds(192));
        var back = tc.Engine.FromOtherEnd(track, fromEnd);

        Assert.Equal(Seconds(28), fromEnd.TimeSpan);
        Assert.Equal(Seconds(192), back.TimeSpan);
    }

    [Fact]
    public void FromOtherEnd_OfAPointPastTheLength_LeavesItForAuthorToRefuse()
    {
        using var tc = TestEngine.Create();
        var track = tc.TrackOfLength(@"C:\music\outro.flac", 220);

        Assert.Equal(Seconds(300), tc.Engine.FromOtherEnd(track, Point.FromSeconds(300)).TimeSpan);
    }

    [Fact]
    public void AConvertedPoint_FadesAtTheSameMoment_AsTheOriginal()
    {
        var a = TestAudio.WriteDcWav("fromend-a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("fromend-b.wav", 4, 0.2);
        float[] RenderWith(Func<TestEngine, FadePointSpec> fadeOutPoint)
        {
            using var tc = TestEngine.Create();
            tc.Add(a, b);
            tc.Engine.Author(tc.AdjacencyOf(a, b), Crossfade(fadeOutPoint(tc)));
            tc.Engine.Play();
            tc.Output.Advance(Rate * 3);
            return tc.Output.Blocks.SelectMany(block => block).ToArray();
        }

        var fromStart = RenderWith(_ => new FadePointSpec(Seconds(1.5), FromEnd: false));
        var fromEnd = RenderWith(tc =>
        {
            var converted = tc.Engine.FromOtherEnd(tc.AdjacencyOf(a, b).Outgoing, Point.FromSeconds(1.5));
            Assert.Equal(Seconds(2.5), converted.TimeSpan);
            return new FadePointSpec(converted.TimeSpan, FromEnd: true);
        });

        Assert.Equal(fromStart, fromEnd);
    }

    [Fact]
    public void CaptureFromEnd_IsTheDistanceFromThePlayheadToTheEnd_FrameExact()
    {
        using var tc = TestEngine.Create(latency: Latency);
        var a = TestAudio.WriteDcWav("capture-end.wav", 4, 0.4);
        tc.Play(a);
        tc.Output.Advance(12345 + Latency);

        var fromStart = tc.Engine.Capture(tc.AdjacencyOf(a, a), CaptureSide.FadeOut)!.Value;
        var fromEnd = tc.Engine.Capture(tc.AdjacencyOf(a, a), CaptureSide.FadeOut, fromEnd: true)!.Value;

        Assert.Equal(12345, Ticks.FramesOf(fromStart.TimeSpan, Rate));
        Assert.Equal(4 * Rate - 12345, Ticks.FramesOf(fromEnd.TimeSpan, Rate));
    }

    [Fact]
    public void CaptureFromEnd_WhenNothingPlays_IsNull()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("capture-end.wav", 4, 0.4);
        tc.Add(a);

        Assert.Null(tc.Engine.Capture(tc.AdjacencyOf(a, a), CaptureSide.FadeOut, fromEnd: true));
    }

    // ── Curve ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Linear_Authored_PlaysLinear_AndTheStoreRoundTripsIt()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("linear-a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("linear-b.wav", 4, 0.2);
        tc.Add(a, b);

        tc.Engine.Author(tc.AdjacencyOf(a, b), Crossfade(Seconds(1), FadeCurveKind.Linear));
        tc.Engine.Play();
        tc.Output.Advance(Rate * 3);

        // Mid-overlap, linear is ½·0.4 + ½·0.2 = 0.3; equal-power would be about 0.42.
        Assert.Equal(0.3, tc.Rendered.Sample(Rate + Rate / 2, 0), 2);
        var reopened = new JsonTransitionStore(tc.Library.TransitionsPath);
        Assert.Equal(FadeCurveKind.Linear, reopened.Get(a, b)!.Curve);
    }

    // ── Blank save ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void ABlankSave_RemovesTheTransition_AndLeavesNoEntryInTheFile()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("blank-a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("blank-b.wav", 4, 0.2);
        tc.Add(a, b);
        var adjacency = tc.AdjacencyOf(a, b);
        tc.Engine.Author(adjacency, Crossfade(Seconds(1)));

        var refusals = tc.Engine.Author(adjacency, new TransitionSpec { Curve = FadeCurveKind.Linear });

        Assert.Empty(refusals);
        Assert.Null(tc.Engine.Transition(adjacency).Global);
        Assert.DoesNotContain("blank-a", File.ReadAllText(tc.Library.TransitionsPath));
        Assert.StartsWith("Removed blank-a", tc.Notices[^1]);
    }

    [Fact]
    public void ABlankSave_OnAnUnauthoredAdjacency_StoresNothing_AndSaysSo()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("blank2-a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("blank2-b.wav", 4, 0.2);
        tc.Add(a, b);
        var adjacency = tc.AdjacencyOf(a, b);

        tc.Engine.Author(adjacency, new TransitionSpec());

        Assert.Null(tc.Engine.Transition(adjacency).Global);
        Assert.False(File.Exists(tc.Library.TransitionsPath) &&
                     File.ReadAllText(tc.Library.TransitionsPath).Contains("blank2-a"));
        Assert.StartsWith("Nothing to save", tc.Notices[^1]);
    }

    // ── Validation ───────────────────────────────────────────────────────────────────────

    public static TheoryData<string, TransitionField, string> ImpossibleValues => new()
    {
        { "fade-out point past the outgoing length", TransitionField.FadeOutPoint, "past the end of short" },
        { "fade-out point from the end, longer than the track", TransitionField.FadeOutPoint, "measured from the end, is longer than short" },
        { "fade-in point past the incoming length", TransitionField.FadeInPoint, "past the end of long" },
        { "negative fade-out duration", TransitionField.FadeOutDuration, "fade-out duration cannot be negative" },
        { "negative fade-in duration", TransitionField.FadeInDuration, "fade-in duration cannot be negative" },
    };

    private static TransitionSpec Impossible(string which) => which switch
    {
        "fade-out point past the outgoing length" => Crossfade(Seconds(3.5)),
        "fade-out point from the end, longer than the track" => Crossfade(new FadePointSpec(Seconds(3.5), FromEnd: true)),
        "fade-in point past the incoming length" => Crossfade(Seconds(1)) with { FadeInPoint = Seconds(4.5) },
        "negative fade-out duration" => Crossfade(Seconds(1)) with { FadeOutDuration = Seconds(-1) },
        _ => Crossfade(Seconds(1)) with { FadeInDuration = Seconds(-1) },
    };

    [Theory]
    [MemberData(nameof(ImpossibleValues))]
    public void AnImpossibleValue_IsRefusedWithItsReason_AndNothingIsStored(
        string which, TransitionField field, string reason)
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("short.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("long.wav", 4, 0.2);
        tc.Add(a, b);
        var adjacency = tc.AdjacencyOf(a, b);
        var saved = Crossfade(Seconds(1));
        tc.Engine.Author(adjacency, saved);

        var refusals = tc.Engine.Author(adjacency, Impossible(which));

        var refusal = Assert.Single(refusals);
        Assert.Equal(field, refusal.Field);
        Assert.Contains(reason, refusal.Reason);
        Assert.Equal(saved, tc.Engine.Transition(adjacency).Global); // the saved one is untouched
        Assert.StartsWith($"Not saved: {adjacency.Name}.", tc.Notices[^1]);
        Assert.Contains(refusal.Reason, tc.Notices[^1]);
    }

    [Fact]
    public void EveryImpossibleValue_IsRefusedAtOnce_EachAgainstItsField()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("short.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("long.wav", 4, 0.2);
        tc.Add(a, b);

        var refusals = tc.Engine.Author(tc.AdjacencyOf(a, b), new TransitionSpec
        {
            FadeOutPoint = Seconds(9),
            FadeOutDuration = Seconds(-1),
            FadeInPoint = Seconds(9),
            FadeInDuration = Seconds(-1),
        });

        Assert.Equal(
            new[] { TransitionField.FadeOutPoint, TransitionField.FadeOutDuration, TransitionField.FadeInPoint, TransitionField.FadeInDuration },
            refusals.Select(r => r.Field));
        Assert.Null(tc.Engine.Transition(tc.AdjacencyOf(a, b)).Global);
    }

    [Fact]
    public void ADurationRunningPastTheEnd_IsSaved_AndShortenedSilently()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("tail-a.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("tail-b.wav", 3, 0.2);
        tc.Add(a, b);
        var spec = Crossfade(Seconds(2.5)) with { FadeOutDuration = Seconds(5) };

        var refusals = tc.Engine.Author(tc.AdjacencyOf(a, b), spec);

        Assert.Empty(refusals);
        Assert.Equal(spec, tc.Engine.Transition(tc.AdjacencyOf(a, b)).Global);
    }

    [Fact]
    public void APointExactlyAtTheLength_IsNotRefused()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("edge-a.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("edge-b.wav", 3, 0.2);
        tc.Add(a, b);
        var adjacency = tc.AdjacencyOf(a, b);
        var spec = Crossfade(new FadePointSpec(adjacency.Outgoing.Length.TimeSpan, FromEnd: true)) with
        {
            FadeInPoint = adjacency.Incoming.Length.TimeSpan,
        };

        Assert.Empty(tc.Engine.Author(adjacency, spec));
        Assert.Equal(spec, tc.Engine.Transition(adjacency).Global);
    }
}
