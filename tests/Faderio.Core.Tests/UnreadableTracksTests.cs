using Faderio.Core.Domain;
using Faderio.Core.Playback;

namespace Faderio.Core.Tests;

/// <summary>Unreadable tracks and decode errors, through the seam (build issue 10). A queued
/// track whose file cannot be read is passed over with a notice and keeps its row, marked; the
/// incoming track opens when its boundary is scheduled; a decode error mid-track ends the track
/// there, as audio that ends early does (ADR 0010).</summary>
public sealed class UnreadableTracksTests
{
    private const int Second = 44100;

    // ── Passed over at play time ─────────────────────────────────────────────────────────

    [Fact]
    public void AFileDeletedBeforeItsTurn_IsPassedOver_WithOneNotice_ItsRowStaysMarked()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 1, 0.3);
        tc.Add(a, b, c);
        File.Delete(b);
        tc.Engine.Play();

        tc.Output.Advance(Second + Second / 2);

        Assert.Equal(c, tc.CurrentTrack!.FilePath);
        Assert.Equal(0.3, tc.Rendered.Sample(Second, 0), 2); // no gap where B would have been
        var notice = Assert.Single(tc.Notices);
        Assert.Contains(Path.GetFileName(b), notice);
        Assert.Equal(new[] { a, b, c }, tc.View.Queue.Select(r => r.Track.FilePath));
        Assert.Equal(new[] { false, true, false }, tc.View.Queue.Select(r => r.IsUnreadable));
    }

    [Fact]
    public void TheIncomingTrack_OpensWhenItsBoundaryIsScheduled_SoLosingItsFileLaterStillPlaysIt()
    {
        // Opened as the outgoing voice starts; the open file does not stop the user deleting
        // or renaming it.
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.2);
        tc.Play(a, b);
        tc.Output.Advance(Second / 2);

        File.Delete(b);
        tc.Output.Advance(Second);

        Assert.Equal(b, tc.CurrentTrack!.FilePath);
        Assert.Equal(0.2, tc.Rendered.DcLevel(Second + Second / 2), 2);
        Assert.Empty(tc.Notices);
    }

    [Fact]
    public void RestoringTheFile_AndReachingItAgain_PlaysIt_AndClearsTheMark()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 1, 0.3);
        tc.Add(a, b, c);
        tc.Engine.SetRepeat(Faderio.Core.Queue.RepeatMode.All);
        File.Move(b, b + ".away");
        tc.Engine.Play();
        tc.Output.Advance(Second + Second / 2); // C, with B passed over

        File.Move(b + ".away", b);
        tc.Output.Advance(Second * 2); // C → A, and A's boundary reaches B again

        Assert.Equal(b, tc.CurrentTrack!.FilePath);
        Assert.Equal(0.2, tc.Rendered.DcLevel(Second * 3 + Second / 2), 2);
        Assert.All(tc.View.Queue, r => Assert.False(r.IsUnreadable));
        Assert.Single(tc.Notices);
    }

    [Fact]
    public void WithAToBAuthored_AndBDeleted_ANeverFadesIntoSilence_ItCutsToC()
    {
        // A → C is unauthored: its own resolution is a hard cut at A's end.
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 3, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 3, 0.3);
        tc.Add(a, b, c);
        tc.Store.Upsert(a, b, CrossfadeAt(1, 1));
        File.Delete(b);
        tc.Engine.Play();

        tc.Output.Advance(Second * 3 + Second / 2);

        Assert.Equal(0.4, tc.Rendered.DcLevel(Second + Second / 2), 2); // no fade where A → B's was
        Assert.Equal(0.4, tc.Rendered.Sample(Second * 3 - 1, 0), 2);
        Assert.Equal(0.3, tc.Rendered.Sample(Second * 3, 0), 2);
        Assert.Equal(c, tc.CurrentTrack!.FilePath);
    }

    [Fact]
    public void WithAToBAuthored_AndBDeleted_AToCPlaysItsOwnTransition()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 3, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 3, 0.3);
        tc.Add(a, b, c);
        tc.Store.Upsert(a, b, CrossfadeAt(1, 1));
        tc.Store.Upsert(a, c, CrossfadeAt(2, 1));
        File.Delete(b);
        tc.Engine.Play();

        tc.Output.Advance(Second * 2 + Second / 2);

        Assert.Equal(0.4, tc.Rendered.DcLevel(Second + Second / 2), 2); // A → B's fade never starts
        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath); // mid A → C's crossfade
        Assert.Equal(c, tc.View.Incoming!.Row.Track.FilePath);
    }

    [Fact]
    public void AQueueChangeWhileAMarkedRowWaits_RaisesNoSecondNotice()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 2, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 1, 0.3);
        tc.Add(a, b, c);
        File.Delete(b);
        tc.Engine.Play();

        tc.Add(TestAudio.WriteDcWav("d.wav", 1, 0.1));
        tc.Engine.SetRepeat(Faderio.Core.Queue.RepeatMode.All);

        Assert.Single(tc.Notices);
    }

    // ── Your own navigation ──────────────────────────────────────────────────────────────

    [Fact]
    public void SkipPassesOverAMarkedRow()
    {
        using var tc = TestEngine.Create();
        var (a, b, c) = ThreeWithTheMiddleDeleted(tc);
        tc.Engine.Play();
        tc.Output.Advance(Second / 4);

        tc.Engine.Next();

        Assert.Equal(c, tc.CurrentTrack!.FilePath);
        Assert.Equal(Playhead.Zero, tc.Playhead);
        Assert.Single(tc.Notices);
    }

    [Fact]
    public void PreviousPassesOverAMarkedRow_Backwards()
    {
        using var tc = TestEngine.Create();
        var (a, b, c) = ThreeWithTheMiddleDeleted(tc);
        tc.Engine.Play();
        tc.Engine.PlayRow(tc.RowOf(c));
        tc.Output.Advance(Second / 4);

        tc.Engine.Previous();

        Assert.Equal(a, tc.CurrentTrack!.FilePath);
    }

    [Fact]
    public void TransitionNowPassesOverAMarkedRow()
    {
        using var tc = TestEngine.Create();
        var (a, b, c) = ThreeWithTheMiddleDeleted(tc);
        tc.Store.Upsert(a, c, CrossfadeAt(3, 1));
        tc.Engine.Play();
        tc.Output.Advance(Second / 4);

        Assert.Equal(tc.AdjacencyOf(a, c), tc.View.NextAdjacency);
        tc.Engine.TransitionNow();
        tc.Output.Advance(Second / 4);

        Assert.Equal(c, tc.View.Incoming!.Row.Track.FilePath);
    }

    [Fact]
    public void DoubleClickingAMarkedRowThatStillFails_LeavesTheCurrentTrackPlaying()
    {
        using var tc = TestEngine.Create();
        var (a, b, c) = ThreeWithTheMiddleDeleted(tc);
        tc.Engine.Play();
        tc.Output.Advance(Second / 4);

        tc.Engine.PlayRow(tc.RowOf(b));
        tc.Output.Advance(Second / 4);

        Assert.Equal(a, tc.CurrentTrack!.FilePath);
        Assert.Equal(0.5, tc.Playhead.TotalSeconds, 2); // carried on, not restarted
        Assert.Equal(Transport.Playing, tc.View.Transport);
        Assert.Equal(2, tc.Notices.Count); // the pass-over, then the failed try
        Assert.True(tc.View.Queue[1].IsUnreadable);
    }

    [Fact]
    public void DoubleClickingAMarkedRowWhoseFileIsBack_PlaysIt_AndClearsTheMark()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        tc.Add(a, b);
        File.Move(b, b + ".away");
        tc.Engine.Play();
        File.Move(b + ".away", b);

        tc.Engine.PlayRow(tc.RowOf(b));

        Assert.Equal(b, tc.CurrentTrack!.FilePath);
        Assert.False(tc.View.Queue[1].IsUnreadable);
    }

    [Fact]
    public void PlayWithEveryRowUnreadable_StaysStopped_WithANoticeForEach()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.2);
        tc.Add(a, b);
        File.Delete(a);
        File.Delete(b);

        tc.Engine.Play();

        Assert.Equal(Transport.Stopped, tc.View.Transport);
        Assert.Equal(2, tc.Notices.Count);
        Assert.All(tc.View.Queue, r => Assert.True(r.IsUnreadable));
    }

    [Fact]
    public void RemovingAMarkedRow_TakesItsMarkWithIt()
    {
        using var tc = TestEngine.Create();
        var (a, b, c) = ThreeWithTheMiddleDeleted(tc);
        tc.Engine.Play();

        tc.Engine.Remove(new[] { tc.RowOf(b) });

        Assert.All(tc.View.Queue, r => Assert.False(r.IsUnreadable));
        Assert.Equal(tc.AdjacencyOf(a, c), tc.View.NextAdjacency);
    }

    // ── A decode error mid-track ─────────────────────────────────────────────────────────

    [Fact]
    public void ADecodeErrorMidTrack_IsOneNotice_AndTheBoundaryFiresThere()
    {
        using var tc = TestEngine.Create();
        var a = CorruptFlac(out var failsAt);
        var b = TestAudio.WriteDcWav("b.wav", 3, 0.2);
        tc.Play(a, b);

        tc.Output.Advance((int)failsAt + Second);

        var notice = Assert.Single(tc.Notices);
        Assert.Contains(Path.GetFileName(a), notice);
        Assert.Contains("could not be read", notice);
        Assert.Equal(0.2, tc.Rendered.Sample(failsAt, 0), 2); // B starts where A failed
        Assert.Equal(b, tc.CurrentTrack!.FilePath);
        Assert.Equal(1.0, tc.Playhead.TotalSeconds, 2);
    }

    [Fact]
    public void ADecodeErrorBeforeTheFadeOutPoint_StartsTheIncomingTrackThere_WithItsAuthoredFadeIn()
    {
        // A fade-out point beyond the error behaves as it does for a short file: the boundary
        // fires where the audio ends, never after a gap.
        using var tc = TestEngine.Create();
        var a = CorruptFlac(out var failsAt);
        var b = TestAudio.WriteDcWav("b.wav", 3, 0.2);
        tc.Store.Upsert(a, b, CrossfadeAt((double)failsAt / Second + 0.25, 1));
        tc.Play(a, b);

        tc.Output.Advance((int)failsAt + Second / 2);

        Assert.Equal(b, tc.CurrentTrack!.FilePath);
        Assert.Equal(0.5, tc.Playhead.TotalSeconds, 2);
        Assert.InRange(tc.Rendered.DcLevel(failsAt + Second / 2), 0.1, 0.19); // fading in, not silent
        Assert.Single(tc.Notices);
    }

    [Fact]
    public void ADecodeErrorInTheOutgoingVoiceMidCrossfade_EndsItThere_AndTheIncomingPlaysOn()
    {
        using var tc = TestEngine.Create();
        var a = CorruptFlac(out var failsAt);
        var b = TestAudio.WriteDcWav("b.wav", 3, 0.2);
        tc.Store.Upsert(a, b, CrossfadeAt((double)failsAt / Second - 0.25, 1));
        tc.Play(a, b);

        tc.Output.Advance((int)failsAt + Second / 4);

        Assert.Single(tc.Notices);
        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath); // the outgoing fell silent at the error
        Assert.Null(tc.View.Incoming);
        Assert.Equal(0.5, tc.Playhead.TotalSeconds, 2);
    }

    [Fact]
    public void AudioEndingBeforeTheFadeOutPoint_StartsTheIncomingTrackThere()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        Truncate(a, 3, 1.0);
        var b = TestAudio.WriteDcWav("b.wav", 3, 0.2);
        tc.Store.Upsert(a, b, CrossfadeAt(2, 1));
        tc.Play(a, b);

        tc.Output.Advance(Second + Second / 2);

        Assert.Equal(b, tc.CurrentTrack!.FilePath);
        Assert.Equal(0.5, tc.Playhead.TotalSeconds, 2);
        Assert.InRange(tc.Rendered.DcLevel(Second + Second / 2), 0.1, 0.19);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────

    private static TransitionSpec CrossfadeAt(double point, double duration) => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(point), FromEnd: false),
        FadeOutDuration = TimeSpan.FromSeconds(duration),
        FadeInPoint = new FadePointSpec(TimeSpan.Zero, FromEnd: false),
        FadeInDuration = TimeSpan.FromSeconds(duration),
    };

    /// <summary>Cuts a 16-bit stereo 44.1 kHz WAV of <paramref name="written"/> seconds to
    /// <paramref name="seconds"/> without touching its header, so the header claims more audio
    /// than the file holds.</summary>
    private static void Truncate(string path, double written, double seconds)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        var headerBytes = stream.Length - (long)(written * Second) * 4;
        stream.SetLength(headerBytes + (long)(seconds * Second) * 4);
    }

    /// <summary>Queues three 4 s tracks and deletes the middle one's file.</summary>
    private static (string A, string B, string C) ThreeWithTheMiddleDeleted(TestEngine tc)
    {
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 4, 0.3);
        tc.Add(a, b, c);
        File.Delete(b);
        return (a, b, c);
    }

    /// <summary>A copy of a checked-in FLAC with 4 KB of its middle overwritten by seeded noise:
    /// the production decoder opens it and then throws partway, a real decode error.
    /// <paramref name="failsAt"/> is how many frames it delivers first, read in the fake output's
    /// block size, measured by decoding it here. (A FLAC cut short instead crashes the decoder
    /// package's process; build issue 21.)</summary>
    private static string CorruptFlac(out long failsAt)
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "noise-44100-16-2.flac");
        Directory.CreateDirectory(TestAudio.Dir);
        var path = Path.Combine(TestAudio.Dir, $"corrupt-{Guid.NewGuid():N}.flac");
        var bytes = File.ReadAllBytes(fixture);
        var rng = new Random(1);
        for (var i = bytes.Length / 2; i < bytes.Length / 2 + 4096; i++)
        {
            bytes[i] = (byte)rng.Next(256);
        }
        File.WriteAllBytes(path, bytes);

        using var source = TrackSources.Open(path);
        var buffer = new float[4410 * 2];
        failsAt = 0;
        try
        {
            int read;
            while ((read = source.Read(buffer, 0, 4410)) > 0)
            {
                failsAt += read;
            }
        }
        catch (Exception)
        {
            return path;
        }
        throw new InvalidOperationException("The damaged FLAC decoded without an error.");
    }
}
