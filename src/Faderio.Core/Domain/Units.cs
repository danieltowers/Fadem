namespace Faderio.Core.Domain;

/// <summary>
/// Time↔frame conversion on an explicit sample rate. Ticks (100 ns) are the interchange unit:
/// the store already serialises <see cref="TimeSpan"/>, and a frame at any promised rate maps
/// to ticks and back without loss (one frame at 96 kHz is 104 ticks). Frames never cross the
/// shell seam (ADR 0003); these three helpers are the only places the conversion happens.
/// </summary>
internal static class Ticks
{
    private const double PerSecond = TimeSpan.TicksPerSecond;

    public static long FramesOf(TimeSpan span, int sampleRate) =>
        (long)Math.Round(span.Ticks * (double)sampleRate / PerSecond);

    public static TimeSpan SpanOf(long frames, int sampleRate) =>
        TimeSpan.FromTicks((long)Math.Round(frames * PerSecond / sampleRate));

    public static int Milliseconds(TimeSpan span) => (int)Math.Round(span.TotalMilliseconds);
}

/// <summary>How long a track is, as the engine issues it (ADR 0010): the span the playhead
/// lives within, the value a from-end point resolves against, and what the seek bar spans.
/// There is exactly one length per track. "Duration" is reserved for fades.</summary>
public readonly record struct Length
{
    internal Length(TimeSpan span) => TimeSpan = span < TimeSpan.Zero ? TimeSpan.Zero : span;

    public TimeSpan TimeSpan { get; }
    public int Milliseconds => Ticks.Milliseconds(TimeSpan);
    public double TotalSeconds => TimeSpan.TotalSeconds;

    public static Length Zero => new(TimeSpan.Zero);

    internal static Length FromFrames(long frames, int sampleRate) => new(Ticks.SpanOf(frames, sampleRate));
    internal long Frames(int sampleRate) => Ticks.FramesOf(TimeSpan, sampleRate);

    public override string ToString() => TimeSpan.ToString(@"m\:ss\.fff");
}

/// <summary>Where the listener is hearing <em>right now</em> within a voice's track. The only
/// position the shell ever sees: displayed, scrubbed and captured. Live and transient; contrast
/// <see cref="Point"/>, which is authored and stored. No conversion from a playhead to a point
/// exists: <c>PlaybackEngine.Capture()</c> is the only sound→point path.</summary>
public readonly record struct Playhead
{
    private Playhead(TimeSpan span) => TimeSpan = span < TimeSpan.Zero ? TimeSpan.Zero : span;

    public TimeSpan TimeSpan { get; }
    public int Milliseconds => Ticks.Milliseconds(TimeSpan);
    public double TotalSeconds => TimeSpan.TotalSeconds;

    public static Playhead Zero => new(TimeSpan.Zero);

    /// <summary>The seek bar's value, straight through: no scaling, ever.</summary>
    public static Playhead FromMilliseconds(int milliseconds) => new(TimeSpan.FromMilliseconds(milliseconds));

    /// <summary>A playhead moved by an offset, for <c>PlaybackEngine.SeekBy</c>; below zero is
    /// zero.</summary>
    internal Playhead Plus(TimeSpan offset) => new(TimeSpan + offset);

    /// <summary>Explicit only: audition an authored point by seeking to it.</summary>
    public static Playhead At(Point point) => new(point.TimeSpan);

    internal static Playhead FromFrames(long frames, int sampleRate) => new(Ticks.SpanOf(frames, sampleRate));
    internal long Frames(int sampleRate) => Ticks.FramesOf(TimeSpan, sampleRate);

    public override string ToString() => TimeSpan.ToString(@"m\:ss\.fff");
}

/// <summary>A position within a track, authored and stored; never a wall-clock time and never
/// an offset into the overlap window. <see cref="FromSeconds"/> is the only text→point path.</summary>
public readonly record struct Point
{
    private Point(TimeSpan span) => TimeSpan = span < TimeSpan.Zero ? TimeSpan.Zero : span;

    public TimeSpan TimeSpan { get; }
    public double TotalSeconds => TimeSpan.TotalSeconds;

    /// <summary>Typed text becomes a point here and nowhere else.</summary>
    public static Point FromSeconds(double seconds) => new(TimeSpan.FromSeconds(seconds));

    internal static Point FromFrames(long frames, int sampleRate) => new(Ticks.SpanOf(frames, sampleRate));
    internal static Point FromTimeSpan(TimeSpan span) => new(span);

    public override string ToString() => TimeSpan.ToString(@"m\:ss\.fff");
}
