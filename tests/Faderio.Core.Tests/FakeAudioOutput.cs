using Faderio.Core.Playback;
using Faderio.Core.Settings;
using NAudio.Wave;

namespace Faderio.Core.Tests;

using PlaybackState = Faderio.Core.Playback.PlaybackState;

/// <summary>
/// The test-side binding of the single injected seam (spec: Testing Decisions): advances the
/// sample position deterministically and captures rendered sample blocks. The engine's mixer
/// is pulled through <see cref="Advance"/>; nothing runs on a real audio device.
/// </summary>
public sealed class FakeAudioOutput : IAudioOutput
{
    public AudioOutputMode? RequestedMode { get; set; }
    public ISampleProvider? Source { get; private set; }
    public PlaybackState State { get; private set; } = PlaybackState.Stopped;

    /// <summary>Frames pulled per clock step: 4410 = 100 ms at the engine's 44.1 kHz.</summary>
    public int BlockFrames { get; init; } = 4410;

    public List<float[]> Blocks { get; } = new();
    public long FramesRendered { get; private set; }
    public int ReadCallCount { get; private set; }

    private bool _stoppedRaised;
    public event EventHandler<OutputStoppedEventArgs>? Stopped;

    public void Initialise(ISampleProvider source, AudioOutputMode mode)
    {
        Source = source;
        RequestedMode = mode;
    }

    public void Play() => State = PlaybackState.Playing;

    public void Pause() => State = PlaybackState.Paused;

    public void Stop() => State = PlaybackState.Stopped;

    /// <summary>Drives the fake sample clock: pulls <paramref name="frames"/> frames from the
    /// source in block-sized steps, capturing every rendered block. A paused or stopped device
    /// pulls nothing — faithful to a real device.</summary>
    public void Advance(int frames)
    {
        if (State != PlaybackState.Playing)
        {
            return;
        }
        var source = Source ?? throw new InvalidOperationException("Output not initialised.");
        var buffer = new float[BlockFrames * 2];
        var remaining = frames;
        while (remaining > 0)
        {
            var toRead = (int)Math.Min(remaining, BlockFrames);
            var read = source.Read(buffer, 0, toRead);
            ReadCallCount++;
            var block = new float[read * 2];
            Array.Copy(buffer, block, block.Length);
            Blocks.Add(block);
            FramesRendered += read;
            remaining -= read;
            if (read < toRead)
            {
                break; // source produced less than asked; the clock waits
            }
        }
    }

    public void Dispose()
    {
        if (!_stoppedRaised)
        {
            _stoppedRaised = true;
            Stopped?.Invoke(this, new OutputStoppedEventArgs(null));
        }
    }
}

public sealed class FakeAudioOutputFactory : IAudioOutputFactory
{
    public List<FakeAudioOutput> Created { get; } = new();

    public IAudioOutput Create(AudioOutputMode requestedMode, Action<string> notifyFallback)
    {
        var output = new FakeAudioOutput { RequestedMode = requestedMode };
        Created.Add(output);
        return output;
    }
}
