using Faderio.Core.Queue;

namespace Faderio.Core.Settings;

/// <summary>What an adjacency nobody authored plays.</summary>
public enum UnauthoredAdjacency
{
    /// <summary>The default: fades never happen unless authored.</summary>
    HardCut,

    /// <summary>Opted in: the default crossfade of <see cref="PlayerSettings.DefaultCrossfadeSeconds"/>.</summary>
    DefaultCrossfade,
}

/// <summary>
/// The library's listening settings (wayfinder ticket 10, D11), persisted as JSON in the
/// library's .faderio\settings.json (ADR 0011); missing keys fall back to these built-in
/// defaults. Each takes effect at once. The output mode is the machine's
/// (<see cref="MachineSettings"/>, library issue 03); a key for it left in an older file is
/// ignored. Shuffle and repeat are the transport's toggles, remembered here per library (library
/// issue 04): <c>SetShuffle</c> and <c>SetRepeat</c> save them, and <c>ChangeSettings</c> keeps
/// the ones in force.
/// </summary>
public sealed record PlayerSettings
{
    public const int CurrentSchemaVersion = 1;
    public const int MinDefaultCrossfadeSeconds = 1;
    public const int MaxDefaultCrossfadeSeconds = 30;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public UnauthoredAdjacency UnauthoredAdjacency { get; init; } = UnauthoredAdjacency.HardCut;

    /// <summary>N, the default crossfade's length in whole seconds, 1 to 30. It never applies to
    /// an authored transition, even one with blank durations.</summary>
    public int DefaultCrossfadeSeconds { get; init; } = 5;

    /// <summary>ReplayGain track gain applied with symmetric clamping. On by default.</summary>
    public bool ReplayGainEnabled { get; init; } = true;

    /// <summary>Shuffle, off by default.</summary>
    public bool Shuffle { get; init; }

    /// <summary>Repeat, off by default.</summary>
    public RepeatMode Repeat { get; init; } = RepeatMode.Off;

    /// <summary>The playlist open when this library was last used, which the next launch opens
    /// (D20); null before any has been. The engine keeps it: <c>ChangeSettings</c> keeps the one
    /// in force (playlists issue 01).</summary>
    public string? LastPlaylist { get; init; }

    /// <summary>These settings as the engine keeps them: the current schema, N brought into
    /// range, and a choice no version knows replaced by its default. A hand-edited file
    /// and a caller alike get the same settings back.</summary>
    public PlayerSettings Normalised() => this with
    {
        SchemaVersion = CurrentSchemaVersion,
        UnauthoredAdjacency = Enum.IsDefined(UnauthoredAdjacency) ? UnauthoredAdjacency : UnauthoredAdjacency.HardCut,
        DefaultCrossfadeSeconds = Math.Clamp(DefaultCrossfadeSeconds, MinDefaultCrossfadeSeconds, MaxDefaultCrossfadeSeconds),
        Repeat = Enum.IsDefined(Repeat) ? Repeat : RepeatMode.Off,
    };
}
