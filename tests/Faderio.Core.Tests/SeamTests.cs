using Faderio.Core.Domain;
using Faderio.Core.Playback;

namespace Faderio.Core.Tests;

/// <summary>
/// Seam S3 (ADR 0002): the engine's public surface end to end, on the fake output. These are
/// the ticket-03 class of defect — a late capture, a playhead that describes sound nobody hears
/// yet — made into assertions. The fake's <c>Latency</c> is the device buffer.
/// </summary>
public sealed class SeamTests
{
    private const int Rate = 44100;
    private const int Latency = 4410; // 100 ms of device buffer, like WASAPI shared

    /// <summary>Where a crossfade between two DC levels audibly began, at or after
    /// <paramref name="from"/>. Over its first few hundred frames an equal-power crossfade moves
    /// the mixed level linearly (the incoming sine's slope; the outgoing cosine is flat), so two
    /// threshold crossings well above the 16-bit noise floor extrapolate back to the onset frame.
    /// A single-threshold detector would place it ~140 frames late.</summary>
    private static double FadeOnset(RenderedAudio rendered, long from, double level)
    {
        long? f1 = null;
        for (var f = from; f < rendered.Frames; f++)
        {
            var deviation = Math.Abs(rendered.Sample(f, 0) - level);
            if (f1 is null && deviation >= 0.001)
            {
                f1 = f;
            }
            else if (f1 is { } first && deviation >= 0.003)
            {
                var d1 = Math.Abs(rendered.Sample(first, 0) - level);
                var slope = (deviation - d1) / (f - first);
                return first - d1 / slope;
            }
        }
        return -1;
    }

    [Fact]
    public void CaptureThenAuthor_TheFadeStartsWhereTheListenerHeardIt_Within1ms()
    {
        // The slice's measured assertion (ticket 05, tightened to 1 ms by ticket 09): with a
        // real device buffer, the decode position runs 100 ms ahead of what is audible. Capture
        // must return the audible position; authoring it and listening back must fade there.
        using var tc = TestEngine.Create(latency: Latency);
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        tc.Play(a, b);
        tc.Output.Advance(66150 + Latency); // rendered 1.6 s; the listener has heard 1.5 s

        var point = tc.Engine.Capture(CaptureSide.FadeOut);
        Assert.NotNull(point);
        Assert.Equal(1.5, point!.Value.TotalSeconds, 4);

        tc.Engine.Author(tc.AdjacencyOf(a, b), new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(point.Value.TimeSpan, FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(0.5),
            FadeInPoint = TimeSpan.Zero,
            FadeInDuration = TimeSpan.FromSeconds(0.5),
        });

        // Listen back: seek to 1.0 s and play through the boundary.
        var seekAtTimeline = tc.Output.FramesRendered;
        tc.Engine.Seek(Playhead.FromMilliseconds(1000));
        tc.Output.Advance(Rate * 2);

        var expectedOnset = seekAtTimeline + 22050; // 1.0 s → 1.5 s is 0.5 s after the seek
        var onset = FadeOnset(tc.Rendered, seekAtTimeline, 0.4);
        Assert.True(onset >= 0, "the fade never started");
        Assert.InRange(onset, expectedOnset - Rate / 1000.0, expectedOnset + Rate / 1000.0); // 1 ms (ADR 0009)
    }

    [Fact]
    public void Capture_ReturnsWhatTheViewShows_Unquantised()
    {
        using var tc = TestEngine.Create(latency: Latency);
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        tc.Play(a);
        tc.Output.Advance(12345 + Latency);

        var point = tc.Engine.Capture(CaptureSide.FadeOut)!.Value;
        Assert.Equal(tc.View.Attended!.Playhead.TimeSpan, point.TimeSpan);
        Assert.Equal(12345, Ticks.FramesOf(point.TimeSpan, Rate)); // frame-exact, not "0.###"
    }

    [Fact]
    public void ThePlayhead_DescribesAudibleSound_NotTheDecodePosition()
    {
        using var tc = TestEngine.Create(latency: Latency);
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        tc.Play(a);
        tc.Output.Advance(Rate); // rendered 1.0 s, audible 0.9 s

        Assert.Equal(0.9, tc.Playhead.TotalSeconds, 3);
    }

    [Fact]
    public void AfterASeek_ThePlayheadStaysHonest_ThroughTheDeviceBufferDrain()
    {
        // ADR 0001: the old audio is still coming out of the device for one buffer; the playhead
        // keeps describing it, then jumps when the new point becomes audible.
        using var tc = TestEngine.Create(latency: Latency);
        var a = TestAudio.WriteDcWav("a.wav", 5, 0.4);
        tc.Play(a);
        tc.Output.Advance(Rate);                          // audible 0.9 s
        tc.Engine.Seek(Playhead.FromMilliseconds(3000));

        Assert.Equal(0.9, tc.Playhead.TotalSeconds, 3);   // still hearing the pre-seek audio
        tc.Output.Advance(Latency / 2);
        Assert.Equal(0.95, tc.Playhead.TotalSeconds, 3);  // draining
        tc.Output.Advance(Latency / 2);
        Assert.Equal(3.0, tc.Playhead.TotalSeconds, 3);   // the seek point is now audible
        tc.Output.Advance(Rate);
        Assert.Equal(4.0, tc.Playhead.TotalSeconds, 3);
    }

    [Fact]
    public void TheAttendedVoice_FlipsAtTheAudibleHandover_InOnePublication()
    {
        // A→B crossfade: fade-out at 1.0 s over 0.5 s. A falls silent at 1.5 s. Until the
        // device has emitted that frame the listener attends to A, whatever the decoder is doing.
        using var tc = TestEngine.Create(latency: Latency);
        var a = TestAudio.WriteToneWav("a.wav", 4, 1000);
        var b = TestAudio.WriteToneWav("b.wav", 4, 2000);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(0.5),
            FadeInPoint = TimeSpan.Zero,
            FadeInDuration = TimeSpan.FromSeconds(0.5),
        });
        tc.Engine.Play();
        tc.Output.Advance(Rate * 3);

        var rowA = tc.RowOf(a);
        var rowB = tc.RowOf(b);
        var views = tc.Sink.Views;
        var flip = views.Select((v, i) => (v, i)).First(x => x.v.Attended?.Row.Id == rowB).i;
        var before = views[flip - 1];
        var at = views[flip];

        // Before the flip the listener attends to A while B is already sounding underneath.
        Assert.Equal(rowA, before.Attended!.Row.Id);
        Assert.True(before.Queue.Single(r => r.Id == rowA).IsCurrent);
        // The flip and IsCurrent move in the same publication, once A has audibly fallen silent.
        Assert.True(at.Queue.Single(r => r.Id == rowB).IsCurrent);
        Assert.False(at.Queue.Single(r => r.Id == rowA).IsCurrent);
        Assert.Null(at.Incoming);
        var emittedAtFlip = tc.EmittedAtPublication[flip];
        Assert.InRange(emittedAtFlip, 66150, 66150 + tc.Output.BlockFrames);

        // And the polled view agreed at the moment of the crossfade: A attended, B incoming.
        Assert.Contains(views, v => v.Attended?.Row.Id == rowA && v.Incoming?.Row.Id == rowB);
    }

    [Fact]
    public void DuringACrossfade_EachVoiceHasItsOwnPlayhead()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 4, 1000);
        var b = TestAudio.WriteToneWav("b.wav", 4, 2000);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(1),
            FadeInPoint = new FadePointSpec(TimeSpan.FromSeconds(0.5), FromEnd: false),
            FadeInDuration = TimeSpan.FromSeconds(1),
        });
        tc.Engine.Play();
        tc.Output.Advance(Rate + 11025); // 1.25 s

        var view = tc.View;
        Assert.Equal(a, view.Attended!.Row.Track.FilePath);
        Assert.Equal(1.25, view.Attended.Playhead.TotalSeconds, 3);
        Assert.Equal(b, view.Incoming!.Row.Track.FilePath);
        Assert.Equal(0.75, view.Incoming.Playhead.TotalSeconds, 3); // started at its 0.5 s point
    }

    [Fact]
    public void Author_ReschedulesAPendingBoundary_DuringTheVeryPlaybackItGoverns()
    {
        // v2 rescheduled only by the accident of a seek. The slice's main loop is: listen,
        // capture, save, hear it — without touching the transport.
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 4, 1000);
        var b = TestAudio.WriteToneWav("b.wav", 4, 2000);
        tc.Play(a, b); // no transition authored: the boundary is a hard cut at 4.0 s
        tc.Output.Advance(22050); // 0.5 s

        tc.Engine.Author(tc.AdjacencyOf(a, b), new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(1),
            FadeInPoint = TimeSpan.Zero,
            FadeInDuration = TimeSpan.FromSeconds(1),
        });
        tc.Output.Advance(Rate); // to 1.5 s

        var gA = tc.Rendered.ToneAmplitude(1000, 66150) / 0.4;
        var gB = tc.Rendered.ToneAmplitude(2000, 66150) / 0.4;
        Assert.InRange(gA, 0.6, 0.8); // the crossfade is in flight at 1.5 s
        Assert.InRange(gB, 0.6, 0.8);
    }

    [Fact]
    public void Unauthor_CancelsAPendingBoundary()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 3, 1000);
        var b = TestAudio.WriteToneWav("b.wav", 3, 2000);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(1),
            FadeInDuration = TimeSpan.FromSeconds(1),
        });
        tc.Engine.Play();
        tc.Output.Advance(22050);
        tc.Engine.Unauthor(tc.AdjacencyOf(a, b));
        tc.Output.Advance(Rate);

        Assert.Equal(0.4, tc.Rendered.ToneAmplitude(1000, 66150), 2); // A plays on, unfaded
        Assert.True(tc.Rendered.ToneAmplitude(2000, 66150) < 0.05);
        Assert.Null(tc.Engine.Transition(tc.AdjacencyOf(a, b)).Global);
    }

    [Fact]
    public void SinkHandlers_RunOutsideTheLock_AndMayReenter()
    {
        var dir = Path.Combine(Path.GetTempPath(), "faderio-engine", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var output = new FakeAudioOutput();
        PlaybackEngine? engine = null;
        var reentered = 0;
        var sink = new ImmediateSink(changed: v =>
        {
            if (v.Transport == Transport.Playing && reentered == 0)
            {
                reentered++;
                engine!.Pause(); // a handler issuing a command: legal because the lock is released
            }
        });
        engine = new PlaybackEngine(output, new Faderio.Core.Transitions.JsonTransitionStore(Path.Combine(dir, "t.json")), sink);
        using var _ = engine;
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        engine.Add(new[] { a }).Wait();
        engine.Play();

        Assert.Equal(1, reentered);
        Assert.Equal(Transport.Paused, engine.View.Transport);
    }

    [Fact]
    public void ChangedIsCoalesced_OnePublicationPerCommand()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.4);
        tc.Add(a, b);
        var before = tc.Sink.Views.Count;
        tc.Engine.Play(); // starts a voice and starts the output: two internal publishes

        Assert.Equal(before + 1, tc.Sink.Views.Count);
        Assert.Equal(Transport.Playing, tc.Sink.Latest!.Transport);
    }
}
