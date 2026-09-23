using Faderio.Core.Playback;

namespace Faderio.Core.Tests;

/// <summary>
/// The test-side binding of the output seam (ADR 0002, S1). It <em>is</em> the clock: the
/// engine's mixer is pulled through <see cref="Advance(int)"/> deterministically, faster than
/// real time, and every rendered block is captured. <see cref="Latency"/> models the device
/// buffer: frames rendered but not yet audible, so <see cref="FramesEmitted"/> lags what has
/// been pulled — exactly what a playhead derived from the device clock must cope with (ADR 0001).
/// </summary>
public sealed class FakeAudioOutput : IAudioOutput
{
    public IFrameSource? Source { get; private set; }
    public Transport State { get; private set; } = Transport.Stopped;

    /// <summary>The engine rate this device declares (ADR 0008). 44.1 kHz keeps every existing
    /// expectation in frames unchanged.</summary>
    public int SampleRate { get; init; } = 44100;

    /// <summary>Frames pulled per clock step: 4410 = 100 ms at 44.1 kHz.</summary>
    public int BlockFrames { get; init; } = 4410;

    /// <summary>Frames the device holds before they become audible. Zero by default, so
    /// emitted equals rendered and expectations read plainly.</summary>
    public int Latency { get; init; }

    public List<float[]> Blocks { get; } = new();
    public long FramesRendered { get; private set; }
    public int ReadCallCount { get; private set; }

    /// <summary>What the listener has actually heard: rendered frames minus the device buffer.</summary>
    public Frames FramesEmitted => new(Math.Max(0, FramesRendered - Latency));

    private bool _stoppedRaised;
    public event Action<Exception?>? Stopped;

    public void Initialise(IFrameSource source) => Source = source;

    public void Play() => State = Transport.Playing;

    public void Pause() => State = Transport.Paused;

    /// <summary>Stops and resets the emitted count: the engine resets its timeline in the same
    /// call, so the two clocks share one origin.</summary>
    public void Stop()
    {
        State = Transport.Stopped;
        FramesRendered = 0;
    }

    /// <summary>Drives the fake sample clock: pulls <paramref name="frames"/> frames from the
    /// source in block-sized steps, capturing every rendered block. A paused or stopped device
    /// pulls nothing — faithful to a real device.</summary>
    public void Advance(int frames)
    {
        if (State != Transport.Playing)
        {
            return;
        }
        var source = Source ?? throw new InvalidOperationException("Output not initialised.");
        var buffer = new float[BlockFrames * 2];
        var remaining = frames;
        while (remaining > 0)
        {
            var toRead = Math.Min(remaining, BlockFrames);
            var read = (int)source.Read(buffer, 0, new Frames(toRead)).Count;
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

    public void Advance(Frames frames) => Advance((int)frames.Count);

    public void Dispose()
    {
        if (!_stoppedRaised)
        {
            _stoppedRaised = true;
            Stopped?.Invoke(null);
        }
    }
}
