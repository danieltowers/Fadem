using NAudio.Wave;

namespace Faderio.Core.Playback;

/// <summary>
/// The output seam bound to a WAV file (ADR 0002, S6): the audible artifact a session can hand
/// the user without launching the shell. Deterministic and faster than real time; like the fake,
/// it is the clock, and it has no latency, so <see cref="FramesEmitted"/> equals what was
/// rendered. 32-bit float, so the file is the engine's output bit for bit.
/// </summary>
public sealed class WavFileAudioOutput : IAudioOutput
{
    private readonly string _path;
    private IFrameSource? _source;
    private WaveFileWriter? _writer;
    private long _rendered;

    public WavFileAudioOutput(string path, int sampleRate = 44100)
    {
        _path = path;
        SampleRate = sampleRate;
    }

    public int SampleRate { get; }

    public Transport State { get; private set; } = Transport.Stopped;

    public Frames FramesEmitted => new(_rendered);

    /// <summary>Frames written to the file over its whole life; unlike <see cref="FramesEmitted"/>
    /// this does not reset on <see cref="Stop"/>.</summary>
    public long FramesWritten { get; private set; }

    public event Action<Exception?>? Stopped;

    public void Initialise(IFrameSource source)
    {
        _source = source;
        _writer = new WaveFileWriter(_path, WaveFormat.CreateIeeeFloatWaveFormat(source.SampleRate, source.Channels));
    }

    public void Play() => State = Transport.Playing;

    public void Pause() => State = Transport.Paused;

    public void Stop()
    {
        State = Transport.Stopped;
        _rendered = 0;
    }

    /// <summary>Pulls <paramref name="frames"/> from the engine and writes them, in blocks the
    /// size of a real device buffer. A paused or stopped output writes nothing.</summary>
    public void Advance(Frames frames, int blockFrames = 4410)
    {
        if (State != Transport.Playing)
        {
            return;
        }
        var source = _source ?? throw new InvalidOperationException("Output not initialised.");
        var writer = _writer ?? throw new InvalidOperationException("Output not initialised.");
        var channels = source.Channels;
        var buffer = new float[blockFrames * channels];
        var remaining = frames.Count;
        while (remaining > 0 && State == Transport.Playing)
        {
            var wanted = (int)Math.Min(remaining, blockFrames);
            var read = (int)source.Read(buffer, 0, new Frames(wanted)).Count;
            writer.WriteSamples(buffer, 0, read * channels);
            _rendered += read;
            FramesWritten += read;
            remaining -= read;
            if (read < wanted)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _writer = null;
        Stopped?.Invoke(null);
    }
}
