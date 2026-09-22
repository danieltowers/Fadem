using Faderio.Core.Settings;
using NAudio.Wave;

namespace Faderio.Core.Playback;

public enum PlaybackState
{
    Stopped,
    Playing,
    Paused,
}

public sealed record OutputStoppedEventArgs(Exception? Error);

/// <summary>
/// The single injected seam: abstracts the output device and its sample clock. Production
/// binds NAudio/WASAPI; tests bind a fake that advances the sample position deterministically
/// and captures rendered sample blocks.
///
/// Read conventions: the engine's mixers and sources use the frames convention (count = stereo
/// frames at 44.1 kHz; the return value is frames rendered). Device bindings must drive the
/// source with that convention — NAudio-based bindings adapt to NAudio's interleaved-samples
/// convention first, via <see cref="FramesToSamplesProvider"/>; the test fake is frames-native.
/// </summary>
public interface IAudioOutput : IDisposable
{
    void Initialise(ISampleProvider source, AudioOutputMode mode);

    void Play();

    void Pause();

    void Stop();

    PlaybackState State { get; }

    /// <summary>Raised when the device stops unexpectedly. Engine errors are surfaced, never
    /// silently swallowed.</summary>
    event EventHandler<OutputStoppedEventArgs>? Stopped;
}

public interface IAudioOutputFactory
{
    /// <summary>Creates an output bound to the requested mode. If WASAPI initialisation fails,
    /// the implementation falls back (WaveOut) and calls <paramref name="notifyFallback"/> —
    /// the downgrade is surfaced, never hidden.</summary>
    IAudioOutput Create(AudioOutputMode requestedMode, Action<string> notifyFallback);
}
