using Faderio.Core.Domain;
using Faderio.Core.Queue;
using Faderio.Core.Settings;
using Faderio.Core.Transitions;
using NAudio.Wave;

namespace Faderio.Core.Playback;

/// <summary>State for the visualiser (issue 0011): fade levels per active voice plus output
/// peak. Level data comes from the engine's own observable state — never UI polling of the
/// audio device. Raised on the rendering thread; the UI marshals or polls the latest value.</summary>
public sealed record VisualiserVoiceLevel(int TrackIndex, double Level);

public sealed record VisualiserState(
    double Peak,
    bool Clipping,
    IReadOnlyList<VisualiserVoiceLevel> Voices);

/// <summary>
/// The transition engine: playback state, effective-transition resolution at boundaries, and
/// sample-accurate fade triggering (spec: modules). All timing is driven from the mixer's
/// sample position — the only injected seam is the audio output boundary.
/// </summary>
public sealed partial class PlaybackEngine
{
    private readonly IAudioOutputFactory _outputFactory;
    private readonly IAudioSourceFactory _sourceFactory;
    private readonly ITransitionStore _store;
    private readonly TransitionResolver _resolver;
    private readonly Random _rng;
    private readonly object _sync = new();
    private readonly TransitionMixer _mixer = new();

    private PlayerSettings _settings;
    private IAudioOutput? _output;
    private LockingOutput? _lockingOutput;
    private MixerVoice? _currentVoice;
    private MixerVoice? _outgoingVoice;
    private BoundarySchedule? _pendingBoundary;
    private PreviewSession? _preview;
    private readonly Dictionary<MixerVoice, int> _voiceTrackIndex = new();
    private IReadOnlyList<Track> _originalOrder = new List<Track>();
    private bool _finishedRaised;
    private bool _disposed;
    private VisualiserState _latestVisualiser = new(0, false, Array.Empty<VisualiserVoiceLevel>());

    public PlaybackEngine(
        IAudioOutputFactory outputFactory,
        IAudioSourceFactory sourceFactory,
        ITransitionStore store,
        TransitionResolver resolver,
        PlayerSettings settings,
        Random? rng = null)
    {
        _outputFactory = outputFactory;
        _sourceFactory = sourceFactory;
        _store = store;
        _resolver = resolver;
        _settings = settings;
        _rng = rng ?? new Random();
        Queue = new PlaybackQueue(_rng);

        _mixer.VoiceEnded += OnVoiceEnded;
        _mixer.BlockRendered += OnBlockRendered;
        RecreateOutput();
    }

    /// <summary>The sample stream the audio output device pulls.</summary>
    public ISampleProvider Output => _lockingOutput ?? throw new InvalidOperationException("Engine not initialised.");

    public PlaybackQueue Queue { get; }

    public PlayerSettings Settings
    {
        get { lock (_sync) { return _settings; } }
    }

    public TransitionResolver Resolver => _resolver;

    public RepeatMode Repeat { get; set; }

    private bool _shuffle;

    public bool Shuffle
    {
        get => _shuffle;
        set
        {
            _shuffle = value;
            lock (_sync)
            {
                if (value)
                {
                    Queue.ShuffleUpcoming();
                }
                else
                {
                    RestoreOriginalOrder();
                }
            }
        }
    }

    public PlaybackState State
    {
        get { lock (_sync) { return _output?.State ?? PlaybackState.Stopped; } }
    }

    public Track? CurrentTrack
    {
        get { lock (_sync) { return _currentVoice is null ? null : Queue.CurrentTrack; } }
    }

    /// <summary>Position within the current track, from the mixer's sample position.</summary>
    public TimeSpan CurrentPosition
    {
        get
        {
            lock (_sync)
            {
                return _currentVoice is null
                    ? TimeSpan.Zero
                    : TimeSpan.FromSeconds(_currentVoice.Source.Position / (double)TransitionMixer.SampleRate);
            }
        }
    }

    public VisualiserState LatestVisualiserState
    {
        get { lock (_sync) { return _latestVisualiser; } }
    }

    public event EventHandler? TrackChanged;

    /// <summary>Fires exactly once per playback run — when the queue plays out (including an
    /// authored end-of-sequence fade) and is not looped by repeat.</summary>
    public event EventHandler? PlaybackFinished;

    /// <summary>Non-blocking status notices (skipped files, output fallback, corrupt settings…).</summary>
    public event EventHandler<string>? Notice;

    /// <summary>Raised on the rendering thread after every rendered block.</summary>
    public event EventHandler<VisualiserState>? VisualiserStateChanged;
}
