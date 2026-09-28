# 07: An authored fade-out is reported as the track ending early

Status: needs-triage
Blocked by: none

Part of the [MusicBee spike](../spec.md). Found in the MusicBee smoke of
[issue 06](06-the-plugin-lets-go-of-the-device-while-stopped.md), 2026-09-28. It is the
engine's, not the plugin's: standalone faderio does the same.

## The problem

An authored crossfade whose fade-out completes before the outgoing track's end (fade-out point
6 s, 2 s long, on a 10 s track) shows the notice

    "tone-a-440.wav" ends 2.0 s before its header claims.

every time the boundary plays. That notice is for a truncated file or a lying header
(ADR 0010, `PlaybackEngineBoundary.NoticeEarlyEnd`). Here the track ends on purpose: the
outgoing voice is removed when its fade completes, and `OnVoiceEnded` finds it neither
`EndSuppressed` nor at its claimed length. A fade-out that runs to the track's end never shows
it, which is why it went unseen.

Reproduced headless at 44100 Hz ("2.0 s") and at 48000 Hz ("1.9 s", rounding) with the test
below, which is red on `a7770f2` and is not committed because of that. The fix should commit it.

## Acceptance criteria

- [ ] An outgoing voice ended by its fade completing raises no early-end notice, at any engine
      rate; a track that really ends short still does.
- [ ] The test below, or its equivalent, is in `Faderio.Core.Tests` and green.
- [ ] The commit states net8.0 and net48 x86 counts.

## The test

```csharp
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
```
