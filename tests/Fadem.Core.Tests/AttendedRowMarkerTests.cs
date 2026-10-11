using Fadem.Core.Playback;

namespace Fadem.Core.Tests;

/// <summary>Interim transport issue 03: the row the view marks as current is the one ▶ would
/// start, in every transport state (glossary: Attended row).</summary>
public sealed class AttendedRowMarkerTests
{
    [Fact]
    public void AtLaunch_TheFirstRowIsMarked()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.2);
        tc.Add(a, b);

        Assert.Equal(Transport.Stopped, tc.View.Transport);
        Assert.Equal(new[] { true, false }, tc.View.Queue.Select(r => r.IsCurrent));
    }

    [Fact]
    public void AfterTheQueueEnds_TheFirstRowIsMarked_AndPlayStartsIt()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.2);
        tc.Play(a, b);
        tc.Output.Advance(44100 * 3);

        Assert.Equal(Transport.Stopped, tc.View.Transport);
        Assert.Equal(new[] { true, false }, tc.View.Queue.Select(r => r.IsCurrent));

        tc.Engine.Play();
        tc.Output.Advance(4410);
        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath);
    }

    [Fact]
    public void AfterTheQueueEnds_MovingTheMarkedRow_PlayStillStartsTheMarkedRow()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 1, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 1, 0.1);
        tc.Play(a, b, c);
        tc.Output.Advance(44100 * 4);
        tc.Engine.Move(new[] { tc.RowOf(a) }, before: null); // a to the bottom, still marked

        Assert.True(tc.View.Queue[^1].IsCurrent);
        tc.Engine.Play();
        tc.Output.Advance(4410);
        Assert.Equal(a, tc.View.Attended!.Row.Track.FilePath);
    }

    [Fact]
    public void AfterTheQueueEnds_UnderShuffle_TheMarkIsOnTheRowPlayStarts()
    {
        using var tc = TestEngine.Create(rngSeed: 7);
        var files = Enumerable.Range(0, 5).Select(i => TestAudio.WriteDcWav($"t{i}.wav", 1, 0.1 * (i + 1))).ToArray();
        tc.Add(files);
        tc.Engine.SetShuffle(true);
        tc.Engine.Play();
        tc.Output.Advance(44100 * 7);
        Assert.Equal(Transport.Stopped, tc.View.Transport);
        var marked = tc.View.Queue.Single(r => r.IsCurrent).Id;

        tc.Engine.Play();
        tc.Output.Advance(4410);
        Assert.Equal(marked, tc.View.Attended!.Row.Id);
    }
}
