namespace Faderio.Core.Settings;

public enum AudioOutputMode
{
    /// <summary>WASAPI shared (default): playback alongside other apps.</summary>
    WasapiShared,

    /// <summary>WASAPI exclusive (opted in): bit-perfect output, takes the device.</summary>
    WasapiExclusive,

    /// <summary>WaveOut: the compatibility fallback, and the third mode the slice must prove on.</summary>
    WaveOut,
}

/// <summary>What an adjacency with no authored transition does.</summary>
public enum DefaultAdjacencyBehaviour
{
    /// <summary>The default: fades never happen unless authored.</summary>
    HardCut,

    /// <summary>Opt-in classic behaviour: a global default crossfade of N seconds.</summary>
    GlobalDefaultCrossfade,
}

/// <summary>What skipping mid-transition does (see CONTEXT.md "Skip").</summary>
public enum SkipBehaviour
{
    /// <summary>The in-flight transition is abandoned right away.</summary>
    AbandonTransition,

    /// <summary>The outgoing fade completes before advancing.</summary>
    LetOutgoingFadeFinish,
}

/// <summary>
/// Player settings per design.md D4. Persisted as JSON at %APPDATA%\faderio\settings.json;
/// missing keys fall back to these built-in defaults.
/// </summary>
public sealed record PlayerSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public AudioOutputMode OutputMode { get; init; } = AudioOutputMode.WasapiShared;
    public DefaultAdjacencyBehaviour DefaultAdjacencyBehaviour { get; init; } = DefaultAdjacencyBehaviour.HardCut;

    /// <summary>The N-second duration used by the global default crossfade and as the fallback
    /// duration for individually optional authored fade durations.</summary>
    public TimeSpan DefaultCrossfadeDuration { get; init; } = TimeSpan.FromSeconds(5);

    public SkipBehaviour SkipBehaviour { get; init; } = SkipBehaviour.AbandonTransition;

    /// <summary>ReplayGain track gain applied with symmetric clamping. On by default.</summary>
    public bool ReplayGainEnabled { get; init; } = true;

    /// <summary>Preview pre-roll in seconds: 2, 5 (default) or 10.</summary>
    public int PreviewPreRollSeconds { get; init; } = 5;

    public bool VisualiserVisible { get; init; } = true;
}

public sealed record SettingsLoadResult(PlayerSettings Settings, string? Warning)
{
    public bool HasWarning => Warning is not null;
}
