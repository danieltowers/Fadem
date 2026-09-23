using Faderio.Core.Playback;
using Faderio.Core.Settings;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Faderio.App.Audio;

/// <summary>
/// The production binding of the output seam: WASAPI shared at 100 ms, WASAPI exclusive at
/// 50 ms, WaveOut at 150 ms. The engine's rate is the device's mix rate, read at construction
/// (ADR 0008); WaveOut declares 44.1 kHz. <see cref="FramesEmitted"/> is measured from the
/// device clock (<see cref="IWavePosition"/>), not a latency constant (ADR 0001); the constant
/// survives only as the fallback for a player that cannot be asked. If WASAPI initialisation
/// fails the downgrade to WaveOut is surfaced, never hidden.
/// </summary>
public sealed class NAudioAudioOutput : IAudioOutput
{
    public const int SharedLatencyMs = 100;
    public const int ExclusiveLatencyMs = 50;
    public const int WaveOutLatencyMs = 150;

    private readonly AudioOutputMode _mode;
    private readonly Action<string> _notify;
    private IWavePlayer? _player;
    private CountingSource? _counting;
    private int _latencyMs;
    private volatile Transport _state = Transport.Stopped;

    public NAudioAudioOutput(AudioOutputMode mode, Action<string> notify)
    {
        _mode = mode;
        _notify = notify;
        SampleRate = mode == AudioOutputMode.WaveOut ? 44100 : QueryMixRate(notify);
    }

    public int SampleRate { get; }

    public Transport State => _state;

    public event Action<Exception?>? Stopped;

    /// <summary>What the listener has heard: the device's own position when it can report one,
    /// clamped to what has been rendered; rendered minus the nominal latency otherwise. Zero
    /// while stopped, so the origin is shared with the engine's timeline.</summary>
    public Frames FramesEmitted
    {
        get
        {
            if (_state == Transport.Stopped || _counting is null)
            {
                return Frames.Zero;
            }
            var rendered = _counting.Rendered;
            if (_player is IWavePosition positioned)
            {
                try
                {
                    var format = positioned.OutputWaveFormat;
                    var frames = positioned.GetPosition() / Math.Max(1, format.BlockAlign);
                    if (format.SampleRate != SampleRate)
                    {
                        frames = frames * SampleRate / format.SampleRate;
                    }
                    return new Frames(Math.Clamp(frames, 0, rendered));
                }
                catch (Exception)
                {
                    // fall through to the estimate
                }
            }
            return new Frames(Math.Max(0, rendered - (long)_latencyMs * SampleRate / 1000));
        }
    }

    public void Initialise(IFrameSource source)
    {
        _counting = new CountingSource(source, this);
        var waveProvider = new FloatToBytesWaveProvider(new FrameSourceSampleProvider(_counting));
        var player = CreatePlayer(_mode, out _latencyMs);
        try
        {
            player.Init(waveProvider);
        }
        catch (Exception e)
        {
            player.Dispose();
            _notify($"{_mode} output unavailable ({e.Message}); falling back to WaveOut.");
            player = new WaveOutEvent { DesiredLatency = WaveOutLatencyMs };
            _latencyMs = WaveOutLatencyMs;
            player.Init(waveProvider);
        }
        player.PlaybackStopped += (_, args) =>
        {
            _state = Transport.Stopped;
            Stopped?.Invoke(args.Exception);
        };
        _player = player;
    }

    public void Play()
    {
        if (_player is null)
        {
            return;
        }
        _counting?.ClearStop();
        _state = Transport.Playing;
        _player.Play();
    }

    public void Pause()
    {
        if (_player is null || _state != Transport.Playing)
        {
            return;
        }
        _state = Transport.Paused;
        _player.Pause();
    }

    /// <summary>Stops and resets the emitted count. The engine calls this from inside a render
    /// when playback ends on its own; stopping the player from its own thread would deadlock,
    /// so in that case the source signals end-of-stream and the player stops itself.</summary>
    public void Stop()
    {
        _state = Transport.Stopped;
        if (_counting is null || _player is null)
        {
            return;
        }
        _counting.Reset();
        if (CountingSource.InRead)
        {
            _counting.RequestStop();
        }
        else
        {
            _player.Stop();
        }
    }

    public void Dispose()
    {
        _player?.Dispose();
        _player = null;
    }

    private static IWavePlayer CreatePlayer(AudioOutputMode mode, out int latencyMs)
    {
        switch (mode)
        {
            case AudioOutputMode.WasapiExclusive:
                latencyMs = ExclusiveLatencyMs;
                return new WasapiOut(AudioClientShareMode.Exclusive, ExclusiveLatencyMs);
            case AudioOutputMode.WaveOut:
                latencyMs = WaveOutLatencyMs;
                return new WaveOutEvent { DesiredLatency = WaveOutLatencyMs };
            default:
                latencyMs = SharedLatencyMs;
                return new WasapiOut(AudioClientShareMode.Shared, SharedLatencyMs);
        }
    }

    private static int QueryMixRate(Action<string> notify)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return device.AudioClient.MixFormat.SampleRate;
        }
        catch (Exception e)
        {
            notify($"Could not read the output device's rate ({e.Message}); assuming 44.1 kHz.");
            return 44100;
        }
    }

    /// <summary>Counts rendered frames (the estimate's basis) and carries the end-of-stream
    /// signal for a stop requested from the render thread.</summary>
    private sealed class CountingSource : IFrameSource
    {
        [ThreadStatic] private static bool _inRead;
        private readonly IFrameSource _inner;
        private readonly NAudioAudioOutput _owner;
        private long _rendered;
        private volatile bool _stopRequested;

        public CountingSource(IFrameSource inner, NAudioAudioOutput owner)
        {
            _inner = inner;
            _owner = owner;
        }

        public static bool InRead => _inRead;

        public long Rendered => Interlocked.Read(ref _rendered);

        public int SampleRate => _inner.SampleRate;

        public int Channels => _inner.Channels;

        public void Reset() => Interlocked.Exchange(ref _rendered, 0);

        public void RequestStop() => _stopRequested = true;

        public void ClearStop() => _stopRequested = false;

        public Frames Read(float[] interleaved, int offset, Frames wanted)
        {
            if (_stopRequested || _owner._state == Transport.Stopped)
            {
                return Frames.Zero; // end of stream: the player stops itself
            }
            _inRead = true;
            try
            {
                var read = _inner.Read(interleaved, offset, wanted);
                Interlocked.Add(ref _rendered, read.Count);
                return read;
            }
            finally
            {
                _inRead = false;
            }
        }
    }
}
