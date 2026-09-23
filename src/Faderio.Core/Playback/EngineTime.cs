using Faderio.Core.Domain;

namespace Faderio.Core.Playback;

/// <summary>Time↔frame conversions on the engine's sample clock, which is the output device's
/// rate, fixed at launch (ADR 0008). Nothing in the engine assumes 44.1 kHz.</summary>
internal static class EngineTime
{
    public static long Frames(TimeSpan t, int sampleRate) => Ticks.FramesOf(t, sampleRate);

    public static TimeSpan TimeSpanOf(long frames, int sampleRate) => Ticks.SpanOf(frames, sampleRate);
}
