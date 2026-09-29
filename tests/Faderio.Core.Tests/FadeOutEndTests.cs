using Faderio.Core.Domain;
using Faderio.Core.Persistence;
using Faderio.Core.Playback;

namespace Faderio.Core.Tests;

/// <summary>An authored fade-out that completes before the outgoing track's end ends that
/// track on purpose: it is not a truncated file, and the early-end notice (ADR 0010) must not
/// name it. Found in the MusicBee smoke of phase 5 issue 06, where every crossfade showed
/// "ends 2.0 s before its header claims".</summary>
public sealed class FadeOutEndTests
{
    private static TransitionSpec FadeOutAt(double seconds, double fade) => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(seconds), FromEnd: false),
        FadeOutDuration = TimeSpan.FromSeconds(fade),
        FadeInPoint = TimeSpan.Zero,
        FadeInDuration = TimeSpan.FromSeconds(fade),
    };

    [Theory]
    [InlineData(44100)]
    [InlineData(48000)]
    public void AnOutgoingTrack_WhoseFadeOutCompletesBeforeItsEnd_IsNotReportedAsEndingEarly(int engineRate)
    {
        var dir = Path.Combine(TestTemp.Root, "engine", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var library = Library.Open(dir);
        var a = TestAudio.WriteDcWav("a.wav", 10, 0.5);
        var b = TestAudio.WriteDcWav("b.wav", 10, 0.3);
        library.Transitions!.Upsert(a, b, FadeOutAt(6, 2));
        var output = new FakeAudioOutput { SampleRate = engineRate };
        var sink = new RecordingSink();
        using var engine = new PlaybackEngine(output, library, sink) { StartAddWorker = work => work() };
        engine.Add(new[] { a, b }).Wait();

        engine.Play();
        output.Advance(12 * engineRate); // past A's fade-out, which completes at 8 s, and into B

        Assert.Equal(b, engine.View.Attended!.Row.Track.FilePath);
        Assert.DoesNotContain(sink.Notices, n => n.Contains("before its header claims"));
    }
}
