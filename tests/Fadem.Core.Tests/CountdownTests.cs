using Fadem.Core.Domain;
using Fadem.Core.Playback;
using Fadem.Core.Queue;

namespace Fadem.Core.Tests;

/// <summary>The remaining time as the readout counts it: the length rounded up to the second,
/// less the playhead cut down to the second, so it ticks with the time taken and the two always
/// sum to the same total.</summary>
public sealed class CountdownTests
{
    private static VoiceView Voice(double playhead, double length) => new(
        new QueueRow(new RowId(1), new Track { FilePath = "a.wav" }, IsCurrent: true),
        Playhead.FromMilliseconds((int)Math.Round(playhead * 1000)),
        new Length(TimeSpan.FromSeconds(length)));

    [Theory]
    [InlineData(10.0, 245.6, 236)]
    [InlineData(10.6, 245.6, 236)] // the exact remainder crosses 235 here; the countdown waits
    [InlineData(10.999, 245.6, 236)]
    [InlineData(11.0, 245.6, 235)] // and ticks with the time taken
    [InlineData(245.3, 245.6, 1)] // never zero while sound is left
    [InlineData(4.5, 5, 1)]
    [InlineData(5, 5, 0)]
    public void TheCountdown_TicksWithTheTimeTaken(double playhead, double length, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), Voice(playhead, length).Countdown.TimeSpan);

    [Fact]
    public void TheTimeTaken_AndTheCountdown_AlwaysSumToTheLengthRoundedUp()
    {
        for (var ms = 0; ms <= 245_600; ms += 50)
        {
            var voice = Voice(ms / 1000.0, 245.6);
            Assert.Equal(246, Math.Floor(voice.Playhead.TotalSeconds) + voice.Countdown.TimeSpan.TotalSeconds);
        }
    }
}
