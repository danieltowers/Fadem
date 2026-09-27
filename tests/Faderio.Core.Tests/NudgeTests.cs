using Faderio.Core.Domain;

namespace Faderio.Core.Tests;

/// <summary>Interim transport issue 06: <c>Nudge</c> moves a point by a delta in whichever frame
/// it is measured (from the start or from the end; both run 0 to the track's length), clamped to
/// the track.</summary>
public sealed class NudgeTests
{
    private static readonly TimeSpan Tenth = TimeSpan.FromSeconds(0.1);

    [Fact]
    public void ATenthEachWay_AndASecond()
    {
        using var tc = TestEngine.Create();
        var track = tc.TrackOfLength("x.wav", 20);
        var twelve = Point.FromSeconds(12);

        Assert.Equal(12.1, tc.Engine.Nudge(track, twelve, Tenth).TotalSeconds, 9);
        Assert.Equal(11.9, tc.Engine.Nudge(track, twelve, -Tenth).TotalSeconds, 9);
        Assert.Equal(13.0, tc.Engine.Nudge(track, twelve, TimeSpan.FromSeconds(1)).TotalSeconds, 9);
    }

    [Fact]
    public void ClampedToTheTrack_AtBothEnds()
    {
        using var tc = TestEngine.Create();
        var track = tc.TrackOfLength("x.wav", 20);

        Assert.Equal(TimeSpan.Zero, tc.Engine.Nudge(track, Point.FromSeconds(0.05), -Tenth).TimeSpan);
        Assert.Equal(track.Length.TimeSpan, tc.Engine.Nudge(track, Point.FromSeconds(19.95), Tenth).TimeSpan);
        Assert.Equal(track.Length.TimeSpan, tc.Engine.Nudge(track, Point.FromSeconds(25), -Tenth).TimeSpan);
    }

    [Fact]
    public void KeepsACapturedPointsPrecision()
    {
        using var tc = TestEngine.Create();
        var track = tc.TrackOfLength("x.wav", 20);
        var captured = Point.FromTimeSpan(TimeSpan.FromTicks(12_345_678)); // 1.2345678 s

        Assert.Equal(TimeSpan.FromTicks(13_345_678), tc.Engine.Nudge(track, captured, Tenth).TimeSpan);
    }
}
