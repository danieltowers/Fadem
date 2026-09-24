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

/// <summary>What an adjacency nobody authored plays.</summary>
public enum UnauthoredAdjacency
{
    /// <summary>The default: fades never happen unless authored.</summary>
    HardCut,

    /// <summary>Opted in: the default crossfade of <see cref="PlayerSettings.DefaultCrossfadeSeconds"/>.</summary>
    DefaultCrossfade,
}

/// <summary>
/// The player's three settings (wayfinder ticket 10), persisted as JSON at
/// %APPDATA%\faderio\settings.json; missing keys fall back to these built-in defaults. The
/// output mode takes effect at the next launch (ADR 0008); the others at once.
/// </summary>
public sealed record PlayerSettings
{
    public const int CurrentSchemaVersion = 1;
    public const int MinDefaultCrossfadeSeconds = 1;
    public const int MaxDefaultCrossfadeSeconds = 30;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public AudioOutputMode OutputMode { get; init; } = AudioOutputMode.WasapiShared;
    public UnauthoredAdjacency UnauthoredAdjacency { get; init; } = UnauthoredAdjacency.HardCut;

    /// <summary>N, the default crossfade's length in whole seconds, 1 to 30. It never applies to
    /// an authored transition, even one with blank durations.</summary>
    public int DefaultCrossfadeSeconds { get; init; } = 5;

    /// <summary>ReplayGain track gain applied with symmetric clamping. On by default.</summary>
    public bool ReplayGainEnabled { get; init; } = true;

    /// <summary>These settings as the engine keeps them: the current schema, N brought into
    /// range, and a mode or choice no version knows replaced by its default. A hand-edited file
    /// and a caller alike get the same settings back.</summary>
    public PlayerSettings Normalised() => this with
    {
        SchemaVersion = CurrentSchemaVersion,
        OutputMode = Enum.IsDefined(OutputMode) ? OutputMode : AudioOutputMode.WasapiShared,
        UnauthoredAdjacency = Enum.IsDefined(UnauthoredAdjacency) ? UnauthoredAdjacency : UnauthoredAdjacency.HardCut,
        DefaultCrossfadeSeconds = Math.Clamp(DefaultCrossfadeSeconds, MinDefaultCrossfadeSeconds, MaxDefaultCrossfadeSeconds),
    };
}
