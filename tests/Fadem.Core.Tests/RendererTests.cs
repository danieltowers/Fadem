using Fadem.Core.Domain;
using Fadem.Core.Playback;
using Fadem.Core.Transitions;
using NAudio.Wave;

namespace Fadem.Core.Tests;

/// <summary>
/// Seam S6's artifact (ticket 11): the rendered file is the engine's output, so what the user
/// hears in the file is what the suite measured in memory.
/// </summary>
public sealed class RendererTests
{
    private static readonly TransitionSpec FadeAt1500ms = new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1.5), FromEnd: false),
        FadeOutDuration = TimeSpan.FromSeconds(0.5),
        FadeInPoint = TimeSpan.Zero,
        FadeInDuration = TimeSpan.FromSeconds(0.5),
    };

    private static (List<float> Left, WaveFormat Format) ReadLeft(string path)
    {
        using var reader = new WaveFileReader(path);
        var left = new List<float>();
        var buffer = new byte[reader.WaveFormat.BlockAlign * 4410];
        int n;
        while ((n = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < n; i += reader.WaveFormat.BlockAlign)
            {
                left.Add(BitConverter.ToSingle(buffer, i));
            }
        }
        return (left, reader.WaveFormat);
    }

    [Fact]
    public void RenderBoundary_WritesPreRollTransitionAndTail_WithTheFadeWhereAuthored()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        tc.Store.Upsert(a, b, FadeAt1500ms);
        var adjacency = new Adjacency(tc.TrackOf(a), tc.TrackOf(b));
        var wav = Path.Combine(tc.TempDir, "boundary.wav");

        var report = Renderer.RenderBoundary(adjacency, tc.Store, wav,
            preRoll: TimeSpan.FromSeconds(1), tail: TimeSpan.FromSeconds(1));

        var (left, format) = ReadLeft(wav);
        Assert.Equal(44100, format.SampleRate);
        Assert.Equal(1000, report.FadeOutPointInFile.Milliseconds);
        Assert.Equal(left.Count, Ticks.FramesOf(report.Rendered.TimeSpan, 44100));

        // Pre-roll: A flat at 0.4 up to the fade-out point, one second into the file.
        Assert.Equal(0.4, left[22050], 3);
        Assert.Equal(0.4, left[44099], 3);
        // The fade starts at 1.000 s in the file, within 1 ms (ADR 0009).
        var onset = Onset(left, 44100 - 4410, 0.4);
        Assert.InRange(onset, 44100 - 44.1, 44100 + 44.1);
        // After the fade B alone is left, and the tail runs at least a second past it.
        Assert.Equal(0.2, left[44100 + 22050 + 4410], 3);
        Assert.True(left.Count >= 44100 + 22050 + 44100, $"rendered only {left.Count} frames");
    }

    [Fact]
    public void TheEngine_RendersABoundaryAsItsLibraryHasIt()
    {
        // The editor's Render button (library issue 01): the engine hands the renderer its store.
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        tc.Store.Upsert(a, b, FadeAt1500ms);
        var adjacency = new Adjacency(tc.TrackOf(a), tc.TrackOf(b));

        var report = tc.Engine.RenderBoundary(adjacency, Path.Combine(tc.TempDir, "engine.wav"));

        Assert.Equal(1500, report.FadeOutPointInFile.Milliseconds); // pre-roll reaches back to 0.0 s
    }

    [Fact]
    public void TheEngine_RendersBlankDurations_AsItsLibrarysSettingSays()
    {
        using var tc = TestEngine.Create(new Settings.PlayerSettings { BlankDurationSeconds = 1 });
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        tc.Store.Upsert(a, b, new TransitionSpec { FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1.5), FromEnd: false) });
        var adjacency = new Adjacency(tc.TrackOf(a), tc.TrackOf(b));
        var wav = Path.Combine(tc.TempDir, "blank.wav");

        var report = tc.Engine.RenderBoundary(adjacency, wav);

        // Both one-second fades are over by 2.5 s: B alone. Five seconds would still be fading A.
        Assert.Equal(1500, report.FadeOutPointInFile.Milliseconds);
        var (left, _) = ReadLeft(wav);
        Assert.Equal(0.2, left[(int)(2.7 * 44100)], 3);
    }

    /// <summary>Round 2 issue 14: a transition with no curve of its own renders in the library's
    /// default curve.</summary>
    [Fact]
    public void TheEngine_RendersABlankCurve_AsItsLibrarysDefaultCurve()
    {
        using var tc = TestEngine.Create(new Settings.PlayerSettings { DefaultCurve = FadeCurveKind.Linear });
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        tc.Store.Upsert(a, b, new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1.5), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(2),
            FadeInDuration = TimeSpan.FromSeconds(2),
        });
        var adjacency = new Adjacency(tc.TrackOf(a), tc.TrackOf(b));
        var wav = Path.Combine(tc.TempDir, "curve.wav");

        var report = tc.Engine.RenderBoundary(adjacency, wav);

        // Half way through the fades, at 2.5 s: linear is half of each, 0.2 + 0.1. Equal-power
        // would be 0.71 of each, 0.42.
        Assert.Equal(1500, report.FadeOutPointInFile.Milliseconds);
        var (left, _) = ReadLeft(wav);
        Assert.Equal(0.3, left[(int)(2.5 * 44100)], 2);
    }

    [Fact]
    public void TheEngine_RendersAnUnauthoredBoundary_WithItsLibrarysDefaultCrossfade()
    {
        using var tc = TestEngine.Create(new Settings.PlayerSettings
        {
            UnauthoredAdjacency = Settings.UnauthoredAdjacency.DefaultCrossfade,
            DefaultCrossfadeSeconds = 1,
        });
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        var adjacency = new Adjacency(tc.TrackOf(a), tc.TrackOf(b));
        var wav = Path.Combine(tc.TempDir, "crossfade.wav");

        var report = tc.Engine.RenderBoundary(adjacency, wav);

        // One second from 3.0 s, not a hard cut at A's end.
        Assert.Equal(3000, report.FadeOutPointInFile.Milliseconds);
        var (left, _) = ReadLeft(wav);
        Assert.Equal(0.4, left[(int)(2.5 * 44100)], 3); // before the fade
        Assert.InRange(left[(int)(3.9 * 44100)], 0.2, 0.3); // A nearly gone, B nearly in
        Assert.Equal(0.2, left[(int)(4.5 * 44100)], 3); // B alone
    }

    [Theory]
    [InlineData(Fadem.Core.Queue.RepeatMode.One, false)]
    [InlineData(Fadem.Core.Queue.RepeatMode.All, true)]
    public void ARender_GoesIntoTheIncomingTrack_WhateverTheLibrarysRepeatAndShuffle(
        Fadem.Core.Queue.RepeatMode repeat, bool shuffle)
    {
        using var tc = TestEngine.Create(new Settings.PlayerSettings { Repeat = repeat, Shuffle = shuffle }, rngSeed: 3);
        var a = TestAudio.WriteDcWav("a.wav", 2, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 2, 0.2);
        var adjacency = new Adjacency(tc.TrackOf(a), tc.TrackOf(b));
        var wav = Path.Combine(tc.TempDir, "repeat.wav");

        var report = tc.Engine.RenderBoundary(adjacency, wav);

        Assert.Equal(2000, report.FadeOutPointInFile.Milliseconds); // a hard cut at A's end
        var (left, _) = ReadLeft(wav);
        Assert.Equal(0.4, left[(int)(1.5 * 44100)], 3);
        Assert.Equal(0.2, left[(int)(2.5 * 44100)], 3); // B, not A again
    }

    [Fact]
    public void AnEngineWithNoLibrary_RendersTheBoundaryUnauthored()
    {
        using var tc = TestEngine.Create();
        using var engine = new PlaybackEngine(new FakeAudioOutput(), store: null, new RecordingSink());
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        var adjacency = new Adjacency(tc.TrackOf(a), tc.TrackOf(b));

        var report = engine.RenderBoundary(adjacency, Path.Combine(tc.TempDir, "none.wav"));

        Assert.Equal(4000, report.FadeOutPointInFile.Milliseconds); // a hard cut at A's end
    }

    [Fact]
    public void TheFile_IsTheSameAudio_TheFakeOutputRendersInMemory()
    {
        // S3's promise: the artifact the human hears is the artifact the suite measured.
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 4, 1000);
        var b = TestAudio.WriteToneWav("b.wav", 4, 2000);
        tc.Store.Upsert(a, b, FadeAt1500ms);
        var adjacency = new Adjacency(tc.TrackOf(a), tc.TrackOf(b));
        var wav = Path.Combine(tc.TempDir, "same.wav");

        var report = Renderer.RenderBoundary(adjacency, tc.Store, wav,
            preRoll: TimeSpan.FromSeconds(1.5), tail: TimeSpan.FromSeconds(1));
        var (file, _) = ReadLeft(wav);

        // The same boundary on the fake: play A from its start (the pre-roll of 1.5 s reaches
        // back to 0.0 s, so the file starts where the track does) and render as many frames.
        tc.Play(a, b);
        tc.Output.Advance(file.Count);
        for (var f = 0; f < file.Count; f += 997)
        {
            Assert.Equal(tc.Rendered.Sample(f, 0), file[f], 6);
        }
        Assert.Equal(0, report.FadeOutPointInFile.Milliseconds - 1500);
    }

    [Fact]
    public void RenderBoundary_RefusesARejectedFile_Loudly()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var bad = Path.Combine(TestAudio.Dir, "x.ogg");
        File.WriteAllText(bad, "not audio");
        var adjacency = new Adjacency(tc.TrackOf(a), new Track { FilePath = bad });

        var e = Assert.Throws<InvalidOperationException>(() =>
            Renderer.RenderBoundary(adjacency, tc.Store, Path.Combine(tc.TempDir, "no.wav")));
        Assert.Contains("x.ogg", e.Message);
    }

    /// <summary>SeamTests' extrapolating onset detector, on a list of samples.</summary>
    private static double Onset(List<float> left, int from, double level)
    {
        int? f1 = null;
        for (var f = from; f < left.Count; f++)
        {
            var deviation = Math.Abs(left[f] - level);
            if (f1 is null && deviation >= 0.001)
            {
                f1 = f;
            }
            else if (f1 is { } first && deviation >= 0.003)
            {
                var d1 = Math.Abs(left[first] - level);
                return first - d1 / ((deviation - d1) / (f - first));
            }
        }
        return -1;
    }
}
