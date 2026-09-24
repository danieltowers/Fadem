using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Queue;

namespace Faderio.Core.Tests;

/// <summary>Build issue 04: <c>Remove(rows)</c> and <c>Clear</c> through the seam. Removing what
/// plays means "not this one", not "stop": playback continues on the row that takes its place,
/// from 0:00, as a hard cut, the way a skip lands.</summary>
public sealed class RemoveAndClearTests
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

    /// <summary>A DC track at 0.1 for its first second and 0.3 after: the rendered level says
    /// whether it was started from its beginning.</summary>
    private static string WriteStepWav(string name) =>
        TestAudio.WriteWav(name, 4, f => f < Second ? 0.1 : 0.3);

    [Fact]
    public void RemovingThreeSelectedRows_OneOfThemPlaying_ContinuesOnTheRowThatTookItsPlace_From0()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.5);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.7);
        var c = WriteStepWav("c.wav");
        var d = TestAudio.WriteDcWav("d.wav", 4, 0.6);
        var e = TestAudio.WriteDcWav("e.wav", 4, 0.8);
        tc.Add(a, b, c, d, e);
        tc.Engine.PlayRow(tc.RowOf(b));
        tc.Output.Advance(Second);

        tc.Engine.Remove(new[] { tc.RowOf(a), tc.RowOf(b), tc.RowOf(d) });

        Assert.Equal(new[] { c, e }, tc.View.Queue.Select(r => r.Track.FilePath));
        Assert.Equal(Transport.Playing, tc.View.Transport);
        Assert.Equal(c, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(0, tc.Playhead.TotalSeconds, 3);
        tc.Output.Advance(Second / 2);
        Assert.Equal(0.5, tc.Playhead.TotalSeconds, 2);
        // A hard cut: C's first second, at once, and nothing of B.
        Assert.Equal(0.1, tc.Rendered.DcLevel(Second + 256), 3);
        Assert.Equal(0.1, tc.Rendered.DcLevel(Second + Second / 2), 3);
        Assert.Equal(new[] { b, c }, tc.AttendedChanges.Select(t => t.FilePath));
    }

    [Fact]
    public void RemovingThePlayingRow_WhilePaused_StaysPaused_OnTheRowThatTookItsPlace()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.5);
        var c = WriteStepWav("c.wav");
        tc.Add(a, c);
        tc.Engine.Play();
        tc.Output.Advance(Second);
        tc.Engine.Pause();

        tc.Engine.Remove(new[] { tc.RowOf(a) });

        Assert.Equal(Transport.Paused, tc.View.Transport);
        Assert.Equal(c, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(0, tc.Playhead.TotalSeconds, 3);
        tc.Output.Advance(Second); // a paused device pulls nothing
        Assert.Equal(Second, tc.Rendered.Frames);

        tc.Engine.Play();
        tc.Output.Advance(Second / 2);
        Assert.Equal(0.1, tc.Rendered.DcLevel(Second + Second / 2), 3);
    }

    [Fact]
    public void RemovingThePlayingRow_WhilePaused_WithDeviceLatency_ShowsTheRowThatTookItsPlace()
    {
        // A paused device emits nothing, so an audible handover would never come: the view
        // would sit on a row that is no longer in the queue until Play.
        using var tc = TestEngine.Create(latency: 4410);
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.5);
        var c = WriteStepWav("c.wav");
        tc.Add(a, c);
        tc.Engine.Play();
        tc.Output.Advance(Second);
        tc.Engine.Pause();

        tc.Engine.Remove(new[] { tc.RowOf(a) });

        Assert.Equal(Transport.Paused, tc.View.Transport);
        Assert.Equal(c, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(0, tc.Playhead.TotalSeconds, 3);
        Assert.True(tc.View.Queue.Single().IsCurrent);
    }

    [Fact]
    public void RemovingTheOutgoingRow_MidTransition_CutsIt_AndTheIncomingPlaysOnAtFullVolume()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 4, FA);
        var b = TestAudio.WriteToneWav("b.wav", 4, FB);
        tc.Add(a, b);
        tc.Store.Upsert(a, b, CrossfadeAt1s(1.0));
        tc.Engine.Play();
        tc.Output.Advance(Second + Second / 4); // 1.25 s: B is fading in under A
        Assert.Equal(b, tc.View.Incoming!.Row.Track.FilePath);

        tc.Engine.Remove(new[] { tc.RowOf(a) });

        Assert.Equal(new[] { b }, tc.View.Queue.Select(r => r.Track.FilePath));
        Assert.Null(tc.View.Incoming);
        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(0.25, tc.Playhead.TotalSeconds, 2); // B carries on where it was
        tc.Output.Advance(Second / 4);
        var end = Second + Second / 2;
        Assert.True(tc.Rendered.ToneAmplitude(FA, end) < 0.05, "the outgoing voice must be cut");
        Assert.Equal(0.4, tc.Rendered.ToneAmplitude(FB, end), 2);
    }

    [Fact]
    public void RemovingTheIncomingRow_MidTransition_CutsIt_AndTheOutgoingPlaysOnAtFullVolume()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 4, FA);
        var b = TestAudio.WriteToneWav("b.wav", 4, FB);
        var c = TestAudio.WriteToneWav("c.wav", 4, FC);
        tc.Add(a, b, c);
        tc.Store.Upsert(a, b, CrossfadeAt1s(1.0));
        tc.Engine.Play();
        tc.Output.Advance(Second + Second / 4);
        Assert.Equal(b, tc.View.Incoming!.Row.Track.FilePath);

        tc.Engine.Remove(new[] { tc.RowOf(b) });

        Assert.Equal(new[] { a, c }, tc.View.Queue.Select(r => r.Track.FilePath));
        Assert.Null(tc.View.Incoming);
        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(1.25, tc.Playhead.TotalSeconds, 2);
        tc.Output.Advance(Second / 4);
        var end = Second + Second / 2;
        Assert.True(tc.Rendered.ToneAmplitude(FB, end) < 0.05, "the incoming voice must be cut");
        Assert.Equal(0.4, tc.Rendered.ToneAmplitude(FA, end), 2); // no fade left on A

        // A plays to its end, then A → C, unauthored, is a hard cut.
        tc.Output.Advance(Second * 3);
        Assert.Equal(c, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(0.4, tc.Rendered.ToneAmplitude(FC, 4 * Second + 2048), 2);
    }

    [Fact]
    public void RemovingBothRows_OfATransition_CutsBoth_AndPlaysTheRowAfterThem_From0()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 4, FA);
        var b = TestAudio.WriteToneWav("b.wav", 4, FB);
        var c = TestAudio.WriteToneWav("c.wav", 4, FC);
        tc.Add(a, b, c);
        tc.Store.Upsert(a, b, CrossfadeAt1s(1.0));
        tc.Engine.Play();
        tc.Output.Advance(Second + Second / 4);

        tc.Engine.Remove(new[] { tc.RowOf(a), tc.RowOf(b) });

        Assert.Equal(c, tc.View.Attended!.Row.Track.FilePath);
        Assert.Null(tc.View.Incoming);
        Assert.Equal(0, tc.Playhead.TotalSeconds, 3);
        tc.Output.Advance(Second / 4);
        var end = Second + Second / 2;
        Assert.True(tc.Rendered.ToneAmplitude(FA, end) < 0.05);
        Assert.True(tc.Rendered.ToneAmplitude(FB, end) < 0.05);
        Assert.Equal(0.4, tc.Rendered.ToneAmplitude(FC, end), 2);
    }

    [Fact]
    public void RemovingTheLastRow_WhileItPlays_FinishesPlayback()
    {
        using var tc = TestEngine.Create(latency: 4410);
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.5);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.7);
        tc.Add(a, b);
        tc.Engine.PlayRow(tc.RowOf(b));
        tc.Output.Advance(Second);

        tc.Engine.Remove(new[] { tc.RowOf(b) });

        Assert.Equal(new[] { a }, tc.View.Queue.Select(r => r.Track.FilePath));
        Assert.Equal(Transport.Stopped, tc.View.Transport);
        Assert.Null(tc.View.Attended);
        Assert.Equal(1, tc.PlaybackFinishedCount);
        var rendered = tc.Rendered.Frames;
        tc.Output.Advance(Second);
        Assert.Equal(rendered, tc.Rendered.Frames); // the device is stopped: nothing more is heard

        tc.Engine.Play(); // a finished run plays again from the top
        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath);
    }

    [Fact]
    public void RemovingTheOnlyRow_WhileItPlays_FinishesPlayback_AndTheQueueIsEmpty()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.5);
        tc.Play(a);
        tc.Output.Advance(Second);

        tc.Engine.Remove(new[] { tc.RowOf(a) });

        Assert.Empty(tc.View.Queue);
        Assert.Equal(Transport.Stopped, tc.View.Transport);
        Assert.Null(tc.View.Attended);
    }

    [Fact]
    public void RemovingTheLastRow_UnderRepeatAll_WrapsToTheFirst_AsASkipWould()
    {
        using var tc = TestEngine.Create();
        var a = WriteStepWav("a.wav");
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.7);
        tc.Add(a, b);
        tc.Engine.SetRepeat(RepeatMode.All);
        tc.Engine.PlayRow(tc.RowOf(b));
        tc.Output.Advance(Second);

        tc.Engine.Remove(new[] { tc.RowOf(b) });

        Assert.Equal(Transport.Playing, tc.View.Transport);
        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(0, tc.Playhead.TotalSeconds, 3);
    }

    [Fact]
    public void RemovingTheNextRow_LeavesPlaybackAlone_AndTheBoundaryFollowsTheNewNext()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 3, FA);
        var b = TestAudio.WriteToneWav("b.wav", 3, FB);
        var c = TestAudio.WriteToneWav("c.wav", 3, FC);
        tc.Add(a, b, c);
        tc.Store.Upsert(a, b, CrossfadeAt1s(1.0));
        tc.Engine.Play();
        tc.Output.Advance(Second / 2);
        var publications = tc.AttendedChanges.Count;

        tc.Engine.Remove(new[] { tc.RowOf(b) });

        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(0.5, tc.Playhead.TotalSeconds, 2);
        Assert.Equal(publications, tc.AttendedChanges.Count);
        // A → B's crossfade is gone with B: A → C is unauthored, so A plays out at full volume
        // and C follows with a hard cut.
        tc.Output.Advance(Second * 2);
        Assert.Equal(0.4, tc.Rendered.ToneAmplitude(FA, 2 * Second), 2);
        Assert.True(tc.Rendered.ToneAmplitude(FB, 2 * Second) < 0.05);
        tc.Output.Advance(Second);
        Assert.Equal(c, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(0.4, tc.Rendered.ToneAmplitude(FC, 3 * Second + 2048), 2);
    }

    [Fact]
    public void RemovingARow_WhileStopped_LeavesTheCurrentRow_AndPlayStartsThere()
    {
        using var tc = TestEngine.Create();
        var paths = Enumerable.Range(0, 5).Select(i => TestAudio.WriteDcWav($"t{i}.wav", 2, 0.1 * (i + 1))).ToArray();
        tc.Add(paths);

        tc.Engine.Remove(new[] { tc.RowOf(paths[1]) });
        tc.Engine.Play();

        Assert.Equal(paths[0], tc.View.Attended!.Row.Track.FilePath);
    }

    [Fact]
    public void RemovingRowsThatAreGone_RemovesTheRest_AndSaysSoOnce()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.5);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.7);
        var rows = tc.Add(a, b);
        tc.Engine.Remove(new[] { rows[0].Id });

        tc.Engine.Remove(new[] { rows[0].Id, rows[1].Id, rows[0].Id });

        Assert.Empty(tc.View.Queue);
        Assert.Single(tc.Notices);
        Assert.Contains("no longer in the queue", tc.Notices[0]);
    }

    [Fact]
    public void RemovingARow_UnderShuffle_AlsoLeavesTheOriginalOrder()
    {
        using var tc = TestEngine.Create(rngSeed: 7);
        var paths = Enumerable.Range(0, 5).Select(i => TestAudio.WriteDcWav($"s{i}.wav", 1, 0.1 * (i + 1))).ToArray();
        tc.Add(paths);
        tc.Engine.SetShuffle(true);

        tc.Engine.Remove(new[] { tc.RowOf(paths[2]) });
        tc.Engine.SetShuffle(false);

        Assert.Equal(new[] { paths[0], paths[1], paths[3], paths[4] }, tc.View.Queue.Select(r => r.Track.FilePath));
    }

    [Fact]
    public void Clear_EmptiesTheQueue_AndStops()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.5);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.7);
        tc.Play(a, b);
        tc.Output.Advance(Second);
        var queueVersion = tc.View.QueueVersion;

        tc.Engine.Clear();

        Assert.Empty(tc.View.Queue);
        Assert.Equal(Transport.Stopped, tc.View.Transport);
        Assert.Null(tc.View.Attended);
        Assert.True(tc.View.QueueVersion > queueVersion);
        tc.Output.Advance(Second);
        Assert.Equal(Second, tc.Rendered.Frames);

        tc.Engine.Play(); // nothing to play
        Assert.Equal(Transport.Stopped, tc.View.Transport);
    }
}
