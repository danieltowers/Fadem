using Faderio.Core.Playback;
using Faderio.Core.Settings;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Faderio.Shell.Audio;

/// <summary>
/// The production binding of the output seam: WASAPI shared at 100 ms, WASAPI exclusive at
/// 50 ms, WaveOut at 150 ms. The device is opened at construction, falling back exclusive →
/// shared → WaveOut with a notice at each step, and the engine's rate is that of the output
/// actually opened (ADR 0008): a WASAPI device's mix rate, or 44.1 kHz on WaveOut.
/// <see cref="FramesEmitted"/> is measured from the device clock (<see cref="IWavePosition"/>),
/// not a latency constant (ADR 0001); the constant survives only as the fallback for a player
/// that cannot be asked.
/// <para>A device that fails mid-play (headphones unplugged) raises <see cref="Stopped"/> with
/// the error and holds what was heard: estimated from what was rendered less the latency, since
/// an invalidated device's clock throws when asked. <see cref="Reopen"/> opens the current default device
/// through the same chain, at the same rate (build issue 11). Every mode converts a rate the
/// device does not run at: shared mode through Windows (NAudio 2.2.1 asks WASAPI for
/// <c>AUTOCONVERTPCM</c>, confirmed on a 48 kHz device), exclusive mode through NAudio's
/// resampler, WaveOut through the Windows mixer.</para>
/// <para>A guest in another player's process lets go of the device while stopped (phase 5
/// issue 06, decision A): with <c>releaseWhileStopped</c> the player is closed at launch once
/// the rate is known, closed after every stop, and opened again at <see cref="Play"/>, from the
/// mode asked for down the same chain, at the engine's rate. A stop from the render thread lets
/// go on the player's own <c>PlaybackStopped</c>, off that thread; a Play that follows a Stop in
/// one command keeps the player it opened. When no mode opens at Play, that is reported as a
/// device loss, off the call, so the next Play reopens with the engine's notice. Standalone
/// faderio keeps the device between plays.</para>
/// </summary>
public sealed class NAudioAudioOutput : IAudioOutput
{
    public const int SharedLatencyMs = 100;
    public const int ExclusiveLatencyMs = 50;
    public const int WaveOutLatencyMs = 150;
    private const int WaveOutRate = 44100;
    private const int Channels = 2; // the engine renders stereo

    private readonly AudioOutputMode _asked;
    private readonly Action<string> _notify;
    private readonly IReadOnlyCollection<AudioOutputMode> _unavailable;
    private readonly bool _releaseWhileStopped;
    private readonly Func<AudioOutputMode, IWavePlayer> _createPlayer;
    private readonly SynchronizationContext? _context;
    private readonly BoundFeed _feed;
    private IWavePlayer? _player;
    private CountingSource? _counting;
    private int _latencyMs;
    private volatile Transport _state = Transport.Stopped;

    /// <summary>What the listener had heard when the device failed, until <see cref="Stop"/>.</summary>
    private long? _heardAtFailure;

    /// <param name="unavailable">Modes to treat as if their device were taken: for smoke runs
    /// of the fallback chain.</param>
    /// <param name="releaseWhileStopped">Let go of the device whenever playback is stopped, and
    /// open it again at <see cref="Play"/>: the MusicBee plugin's way, so MusicBee's own player
    /// has the device back after faderio's Stop, in exclusive mode too.</param>
    public NAudioAudioOutput(
        AudioOutputMode mode,
        Action<string> notify,
        IReadOnlyCollection<AudioOutputMode>? unavailable = null,
        bool releaseWhileStopped = false)
        : this(mode, notify, unavailable, releaseWhileStopped, CreatePlayer)
    {
    }

    /// <summary>For tests: <paramref name="createPlayer"/> stands in for NAudio's players.</summary>
    internal NAudioAudioOutput(
        AudioOutputMode mode,
        Action<string> notify,
        IReadOnlyCollection<AudioOutputMode>? unavailable,
        bool releaseWhileStopped,
        Func<AudioOutputMode, IWavePlayer> createPlayer)
    {
        _asked = mode;
        _notify = notify;
        _unavailable = unavailable ?? Array.Empty<AudioOutputMode>();
        _releaseWhileStopped = releaseWhileStopped;
        _createPlayer = createPlayer;
        _context = SynchronizationContext.Current; // where NAudio's players post their stop
        _feed = new BoundFeed();
        if (OpenFirstAvailable(mode, engineRate: null) is { } opened)
        {
            Mode = opened.Mode;
        }
        SampleRate = _feed.Format.SampleRate; // the rate the player was opened for
        if (_releaseWhileStopped)
        {
            ClosePlayer(); // the rate is known; the device is opened again at Play
        }
    }

    /// <summary>The modes tried in turn when the one asked for cannot be opened, at launch and on
    /// reopening: exclusive → shared → WaveOut, from the mode asked (build issue 11).</summary>
    public static IReadOnlyList<AudioOutputMode> Fallbacks(AudioOutputMode asked) => asked switch
    {
        AudioOutputMode.WasapiExclusive =>
            new[] { AudioOutputMode.WasapiExclusive, AudioOutputMode.WasapiShared, AudioOutputMode.WaveOut },
        AudioOutputMode.WaveOut => new[] { AudioOutputMode.WaveOut },
        _ => new[] { AudioOutputMode.WasapiShared, AudioOutputMode.WaveOut },
    };

    public int SampleRate { get; }

    /// <summary>The mode in use: what the title names. Null when no output could be opened.</summary>
    public AudioOutputMode? Mode { get; private set; }

    /// <summary>Raised when reopening lands on another mode than the one in use.</summary>
    public event Action? ModeChanged;

    public Transport State => _state;

    public event Action<Exception?>? Stopped;

    /// <summary>What the listener has heard: the device's own position when it can report one,
    /// clamped to what has been rendered; rendered minus the nominal latency otherwise. Zero
    /// while stopped, so the origin is shared with the engine's timeline, except after a failure,
    /// when it holds what was heard until <see cref="Stop"/>.</summary>
    public Frames FramesEmitted =>
        _heardAtFailure is { } heard ? new Frames(heard) : MeasureHeard();

    public void Initialise(IFrameSource source)
    {
        if (source.SampleRate != SampleRate)
        {
            throw new InvalidOperationException($"The source runs at {source.SampleRate} Hz; this output at {SampleRate} Hz.");
        }
        _counting = new CountingSource(source, this);
        _feed.Inner = new FloatToBytesWaveProvider(new FrameSourceSampleProvider(_counting));
        if (Mode is null)
        {
            // Nothing opened at launch: the engine treats it as a lost device, and Play reopens.
            Stopped?.Invoke(new InvalidOperationException("no audio output could be opened"));
        }
    }

    public void Play()
    {
        if (_player is null && !OpenForPlay())
        {
            return;
        }
        _counting?.ClearStop();
        _state = Transport.Playing;
        _player!.Play();
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
    /// so in that case the source signals end-of-stream and the player stops itself. An output
    /// that lets go while stopped closes the player here, or, from the render thread, on the
    /// player's own <c>PlaybackStopped</c>.</summary>
    public void Stop()
    {
        _state = Transport.Stopped;
        _heardAtFailure = null;
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
            if (_releaseWhileStopped)
            {
                ClosePlayer();
            }
        }
    }

    /// <summary>Closes the failed player and opens the current default device, from the mode in
    /// use down the fallback chain, at the engine's rate. Returns the device's own rate: a WASAPI
    /// device's mix rate, or the engine's on WaveOut, which cannot be asked.</summary>
    public int Reopen()
    {
        ClosePlayer();
        _state = Transport.Stopped;
        _heardAtFailure = null;
        _counting?.Reset();
        _counting?.ClearStop();
        var inUse = Mode;
        if (OpenFirstAvailable(inUse ?? _asked, SampleRate) is not { } opened)
        {
            throw new InvalidOperationException("no audio output could be opened");
        }
        if (opened.Mode != inUse)
        {
            Mode = opened.Mode;
            ModeChanged?.Invoke();
        }
        return opened.DeviceRate;
    }

    public void Dispose() => ClosePlayer();

    /// <summary>Opens the device for a Play while none is open, which only an output that lets
    /// go while stopped leaves so: from the mode asked for, since the device MusicBee's player
    /// held at launch may be free now. When no mode opens, the failure is reported as a player
    /// that fails after starting reports it, off this call: the engine parks where the listener
    /// was, with a notice, and its next Play reopens.</summary>
    private bool OpenForPlay()
    {
        if (!_releaseWhileStopped || Mode is null)
        {
            return false;
        }
        var inUse = Mode;
        if (OpenFirstAvailable(_asked, SampleRate) is { } opened)
        {
            if (opened.Mode != inUse)
            {
                Mode = opened.Mode;
                ModeChanged?.Invoke();
            }
            return true;
        }
        // As a player that started and then failed: playing until its failure lands.
        _state = Transport.Playing;
        Post(() => Fail(new InvalidOperationException("no audio output could be opened")));
        return false;
    }

    /// <summary>The device failed: what was heard is held, and the engine is told.</summary>
    private void Fail(Exception error)
    {
        _heardAtFailure = MeasureHeard().Count;
        _state = Transport.Stopped;
        Stopped?.Invoke(error);
    }

    /// <summary>Runs <paramref name="action"/> off the calling thread, where NAudio's players
    /// deliver their stop: the UI thread when there is one, a pool thread otherwise.</summary>
    private void Post(Action action)
    {
        if (_context is null)
        {
            ThreadPool.QueueUserWorkItem(_ => action());
        }
        else
        {
            _context.Post(_ => action(), null);
        }
    }

    private void ClosePlayer()
    {
        // Its last PlaybackStopped is no longer this output's; closed once, from whichever thread
        // gets there first (Stop on the UI thread, or the render thread's own stop).
        var player = Interlocked.Exchange(ref _player, null);
        try
        {
            player?.Dispose();
        }
        catch (Exception e)
        {
            // A device that has gone can fail to close; the output is being replaced anyway.
            _notify($"Closing the audio output failed ({e.Message}).");
        }
    }

    /// <summary>Opens the first mode down the chain from <paramref name="from"/> that the
    /// current default device allows, with a notice for each that fails. The feed runs at
    /// <paramref name="engineRate"/>, or at launch at the device's own rate. Returns the mode
    /// opened and the device's rate: the format an exclusive stream actually opened at, a shared
    /// device's mix rate, or on WaveOut, which cannot be asked, the feed's. Null when none opens.
    /// </summary>
    private (AudioOutputMode Mode, int DeviceRate)? OpenFirstAvailable(AudioOutputMode from, int? engineRate)
    {
        foreach (var candidate in Fallbacks(from))
        {
            try
            {
                var deviceRate = candidate == AudioOutputMode.WaveOut ? engineRate ?? WaveOutRate : QueryMixRate();
                _feed.Format = WaveFormat.CreateIeeeFloatWaveFormat(engineRate ?? deviceRate, Channels);
                _player = Open(candidate);
                if (candidate == AudioOutputMode.WasapiExclusive && _player is WasapiOut exclusive)
                {
                    deviceRate = exclusive.OutputWaveFormat.SampleRate;
                }
                return (candidate, deviceRate);
            }
            catch (Exception e)
            {
                NoticeFallback(candidate, e);
            }
        }
        return null;
    }

    /// <summary>Opens <paramref name="mode"/> on the current default device for the feed's
    /// format, or throws.</summary>
    private IWavePlayer Open(AudioOutputMode mode)
    {
        if (_unavailable.Contains(mode))
        {
            throw new InvalidOperationException("the device is in use (--unavailable)");
        }
        var player = _createPlayer(mode);
        try
        {
            player.Init(_feed);
        }
        catch
        {
            player.Dispose();
            throw;
        }
        player.PlaybackStopped += (sender, args) =>
        {
            if (!ReferenceEquals(sender, _player))
            {
                return;
            }
            // A clean stop is always one this output asked for, and Stop already set the state.
            // It is posted to the UI thread, so it can arrive after a Play that followed the Stop
            // in the same command (a preview starting, build issue 16): it must not stop that.
            if (args.Exception is not null)
            {
                _heardAtFailure = MeasureHeard().Count;
                _state = Transport.Stopped;
            }
            else if (_releaseWhileStopped && _state == Transport.Stopped)
            {
                // The stream ended on a stop asked for from the render thread: let go now, off it.
                ClosePlayer();
            }
            Stopped?.Invoke(args.Exception);
        };
        _latencyMs = LatencyOf(mode);
        return player;
    }

    private void NoticeFallback(AudioOutputMode failed, Exception e)
    {
        var chain = Fallbacks(failed);
        var next = chain.Count > 1 ? $"trying {OutputModeNames.Of(chain[1])}" : "no audio output could be opened";
        _notify($"{OutputModeNames.Of(failed)} unavailable ({e.Message}); {next}.");
    }

    private Frames MeasureHeard()
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
                return new Frames(Math.Min(Math.Max(frames, 0), rendered));
            }
            catch (Exception)
            {
                // fall through to the estimate
            }
        }
        return new Frames(Math.Max(0, rendered - (long)_latencyMs * SampleRate / 1000));
    }

    private static IWavePlayer CreatePlayer(AudioOutputMode mode) => mode switch
    {
        AudioOutputMode.WasapiExclusive => new WasapiOut(AudioClientShareMode.Exclusive, ExclusiveLatencyMs),
        AudioOutputMode.WaveOut => new WaveOutEvent { DesiredLatency = WaveOutLatencyMs },
        _ => new WasapiOut(AudioClientShareMode.Shared, SharedLatencyMs),
    };

    private static int LatencyOf(AudioOutputMode mode) => mode switch
    {
        AudioOutputMode.WasapiExclusive => ExclusiveLatencyMs,
        AudioOutputMode.WaveOut => WaveOutLatencyMs,
        _ => SharedLatencyMs,
    };

    /// <summary>The current default device's mix rate; throws when there is no device.</summary>
    private static int QueryMixRate()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        return device.AudioClient.MixFormat.SampleRate;
    }

    /// <summary>The player's input: its format is fixed when a player is opened, before the
    /// engine exists, and the engine's source is bound to it by <see cref="Initialise"/>.</summary>
    private sealed class BoundFeed : IWaveProvider
    {
        public WaveFormat Format { get; set; } = WaveFormat.CreateIeeeFloatWaveFormat(WaveOutRate, Channels);

        public IWaveProvider? Inner { get; set; }

        public WaveFormat WaveFormat => Format;

        public int Read(byte[] buffer, int offset, int count)
        {
            if (Inner is { } inner)
            {
                return inner.Read(buffer, offset, count);
            }
            Array.Clear(buffer, offset, count);
            return count;
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
