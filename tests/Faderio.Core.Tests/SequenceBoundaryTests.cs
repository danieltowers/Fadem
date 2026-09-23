using Faderio.Core.Domain;
using Faderio.Core.Playback;

namespace Faderio.Core.Tests;

/// <summary>End-of-sequence and non-overlapping fade shapes.</summary>
public sealed class SequenceBoundaryTests
{
    private const double FA = 1000;
    private const double FB = 2000;

    [Fact]
    public void EndOfQueue_AuthoredFadeOutPlaysOut_ThenPlaybackStops()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 4, FA);
        var b = TestAudio.WriteToneWav("b.wav", 4, FB);
        tc.Add(a); // B is not queued: the fade-out is honoured with no incoming track
        tc.Store.Upsert(a, b, new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(2), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(1),
        });
        tc.Engine.Play();
        tc.Output.Advance(44100 * 4);

        // Full until the authored fade-out point at 2.0 s…
        Assert.Equal(0.4, tc.Rendered.ToneAmplitude(FA, 88200), 2);
        // …mid-fade at 2.5 s…
        Assert.InRange(tc.Rendered.ToneAmplitude(FA, 110250), 0.2, 0.32);
        // …silent after the fade completes at 3.0 s…
        Assert.Equal(0.0, tc.Rendered.ToneAmplitude(FA, 132300), 2);
        // …and playback finished exactly once: a faded-out voice has fallen silent, so the
        // run ends at the fade's end, not a second later when the file would have run out.
        Assert.Equal(1, tc.PlaybackFinishedCount);
        Assert.Equal(Transport.Stopped, tc.View.Transport);
    }

    [Fact]
    public void NonOverlappingFades_OutgoingCompletesBeforeIncomingBegins()
    {
        // Fade-in only: no outgoing fade, so A completes before B begins.
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        var b = TestAudio.WriteToneWav("b.wav", 4, FB);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, new TransitionSpec
        {
            FadeOutDuration = TimeSpan.Zero,
            FadeInPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
            FadeInDuration = TimeSpan.FromSeconds(1),
        });
        tc.Engine.Play();
        tc.Output.Advance(44100 * 5);

        // A plays flat right up to its end.
        Assert.Equal(0.4, tc.Rendered.Sample(132299, 0), 2);
        // B does not start before A completes…
        Assert.True(tc.Rendered.ToneAmplitude(FB, 132300) < 0.05,
            $"B must not start before A ends (was {tc.Rendered.ToneAmplitude(FB, 132300):F3})");
        // …then starts exactly at the authored fade-in point (1.0 s into B): the tone is at
        // nearly full fade-in gain by 0.68 s into B's voice.
        Assert.True(tc.Rendered.ToneAmplitude(FB, 132300 + 30000) > 0.3,
            $"B tone too quiet (was {tc.Rendered.ToneAmplitude(FB, 132300 + 30000):F3})");
        Assert.Equal(b, tc.CurrentTrack!.FilePath);
    }
}
