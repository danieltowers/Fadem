using Faderio.Core.Domain;
using Faderio.Core.Settings;

namespace Faderio.Core.Tests;

/// <summary>Seek and skip interaction semantics through the seam.</summary>
public sealed class SeekSkipSemanticsTests
{
    private const double FA = 1000;
    private const double FB = 2000;

    private static TransitionSpec CrossfadeAt1s(double duration) => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
        FadeInPoint = TimeSpan.Zero,
        FadeOutDuration = TimeSpan.FromSeconds(duration),
        FadeInDuration = TimeSpan.FromSeconds(duration),
    };

    [Fact]
    public void SeekDuringACrossfade_CancelsTheTransition_AndScrubsTheAttendedVoice()
    {
        // ADR 0001: the attended voice is the outgoing one until it falls silent, so a seek
        // mid-crossfade scrubs the *outgoing* track and drops the incoming voice.
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 4, FA);
        var b = TestAudio.WriteToneWav("b.wav", 4, FB);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, CrossfadeAt1s(1.0));
        tc.Engine.Play();
        tc.Output.Advance(44100 + 11025); // 1.25 s: B is mid fade-in

        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(b, tc.View.Incoming!.Row.Track.FilePath);

        tc.Engine.Seek(Playhead.FromMilliseconds(2000));
        tc.Output.Advance(11025); // 0.25 s more

        Assert.Null(tc.View.Incoming);
        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(2.25, tc.Playhead.TotalSeconds, 2);
        var end = 44100 + 11025 + 11025;
        Assert.InRange(tc.Rendered.ToneAmplitude(FA, end) / 0.4, 0.95, 1.05); // A full, no fade
        Assert.True(tc.Rendered.ToneAmplitude(FB, end) < 0.05, "the incoming voice must be gone");
    }

    [Fact]
    public void SeekPastTheAuthoredFadeOutPoint_DoesNotForceTheTransition()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 3, FA);
        var b = TestAudio.WriteToneWav("b.wav", 3, FB);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, CrossfadeAt1s(1.0));
        tc.Engine.Play();
        tc.Output.Advance(22050);                          // 0.5 s
        tc.Engine.Seek(Playhead.FromMilliseconds(2000));  // past the 1.0 s fade-out point
        tc.Output.Advance(44100);                          // 1.0 s more (rendered 1.5 s total)

        // A plays flat from the seek point: rendered 1.0 s ↔ source 2.5 s, well past where
        // the fade would have started.
        Assert.Equal(0.4, tc.Rendered.ToneAmplitude(FA, 44100), 2);

        // A's remaining second plays out; B begins with a plain hard cut at rendered 1.5 s.
        tc.Output.Advance(44100 * 2);
        Assert.Equal(0.4, tc.Rendered.ToneAmplitude(FB, 132300 + 2000), 2);
    }

    [Fact]
    public void SkipMidTransition_AbandonsIt_AndStartsTheNextAdjacency()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 4, FA);
        var b = TestAudio.WriteToneWav("b.wav", 4, FB);
        var c = TestAudio.WriteToneWav("c.wav", 4, 3000);
        tc.Add(a, b, c);
        tc.Store.Upsert(a, b, CrossfadeAt1s(1.0));
        // B→C is authored with no outgoing fade and a 1 s fade-in (B's own adjacency).
        tc.Store.Upsert(b, c, new TransitionSpec
        {
            FadeOutDuration = TimeSpan.Zero,
            FadeInDuration = TimeSpan.FromSeconds(1),
        });

        tc.Engine.Play();
        tc.Output.Advance(44100 + 11025); // 1.25 s, mid-crossfade
        tc.Engine.Next();
        tc.Output.Advance(11025); // 0.25 s later

        // The outgoing track is gone; the incoming track plays at full volume and is attended.
        Assert.True(tc.Rendered.ToneAmplitude(FA, 66150) < 0.05,
            $"outgoing must be gone (was {tc.Rendered.ToneAmplitude(FA, 66150):F3})");
        Assert.Equal(0.4, tc.Rendered.ToneAmplitude(FB, 66150), 2);
        Assert.Equal(b, tc.CurrentTrack!.FilePath);

        // The next adjacency begins with its own transition: B runs to its end (no fade),
        // then C starts (at 5.0 s on the timeline — B began at 1.0 s), fading in over its
        // authored 1 s: at +15000 frames the gain is sin(π/4)·0.4 ≈ 0.2.
        tc.Output.Advance(44100 * 4);
        Assert.InRange(tc.Rendered.ToneAmplitude(3000, 220500 + 15000), 0.15, 0.25);
    }

    [Fact]
    public void SkipWithLetFinishSetting_LetsTheOutgoingFadeComplete()
    {
        var settings = new PlayerSettings { SkipBehaviour = SkipBehaviour.LetOutgoingFadeFinish };
        using var tc = TestEngine.Create(settings);
        var a = TestAudio.WriteToneWav("a.wav", 4, FA);
        var b = TestAudio.WriteToneWav("b.wav", 4, FB);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, CrossfadeAt1s(1.0));

        tc.Engine.Play();
        tc.Output.Advance(44100 + 11025); // 1.25 s, mid-crossfade
        tc.Engine.Next();

        // The outgoing fade continues: at 1.75 s (frame 77175) its gain is
        // cos(π/2 · 0.75) ≈ 0.38 → amplitude ≈ 0.15.
        tc.Output.Advance(22050);
        Assert.InRange(tc.Rendered.ToneAmplitude(FA, 77175), 0.12, 0.19);
        Assert.True(tc.Rendered.ToneAmplitude(FB, 77175) / 0.4 > 0.9);

        // After the fade completes at 2.0 s the outgoing voice is gone.
        tc.Output.Advance(22050);
        Assert.True(tc.Rendered.ToneAmplitude(FA, 88200) < 0.05);
        Assert.Equal(0.4, tc.Rendered.ToneAmplitude(FB, 88200), 2);
    }
}
