namespace Faderio.Core.Playback;

/// <summary>Time↔frame conversions on the engine's fixed sample clock.</summary>
internal static class EngineTime
{
    public const int SampleRate = TransitionMixer.SampleRate;

    public static long Frames(TimeSpan t) =>
        (long)Math.Round(t.TotalSeconds * SampleRate);

    public static TimeSpan TimeSpanOf(long frames) =>
        TimeSpan.FromSeconds(frames / (double)SampleRate);
}
