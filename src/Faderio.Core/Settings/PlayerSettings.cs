using System.Text.Json.Serialization;
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

/// <summary>Which ReplayGain a track plays at, as MusicBee offers it.</summary>
public enum ReplayGainMode
{
    /// <summary>Every track at unity.</summary>
    Off,

    /// <summary>Each track at its track gain: the default.</summary>
    Track,

    /// <summary>Each track at its album gain, or its track gain when it has none.</summary>
    Album,

    /// <summary>Album gain for a track the queue plays beside another of its album, with shuffle
    /// off; track gain otherwise.</summary>
    Smart,
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
    public const int MinBlankDurationSeconds = 0;
    public const int MaxBlankDurationSeconds = 30;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public UnauthoredAdjacency UnauthoredAdjacency { get; init; } = UnauthoredAdjacency.HardCut;

    /// <summary>N, the default crossfade's length in whole seconds, 1 to 30. It never applies to
    /// an authored transition, even one with blank durations.</summary>
    public int DefaultCrossfadeSeconds { get; init; } = 5;

    /// <summary>What an authored transition's blank fade-out or fade-in duration plays, in whole
    /// seconds, 0 to 30. It applies only to a duration left blank; one typed in is kept.</summary>
    public int BlankDurationSeconds { get; init; } = 5;

    /// <summary>Which ReplayGain tracks play at, clamped symmetrically: track gain by default.</summary>
    public ReplayGainMode ReplayGainMode { get; init; } = ReplayGainMode.Track;

    /// <summary>The on/off switch that came before <see cref="ReplayGainMode"/>: read from an
    /// older file, where false means <see cref="ReplayGainMode.Off"/>, and never written.</summary>
    [JsonPropertyName("replayGainEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? FormerReplayGainEnabled { get; init; }

    /// <summary>Shuffle, off by default.</summary>
    public bool Shuffle { get; init; }

    /// <summary>Repeat, off by default.</summary>
    public RepeatMode Repeat { get; init; } = RepeatMode.Off;

    /// <summary>Whether the editor follows playback, off by default: each track the listener
    /// comes to opens the editor on its this → next (<see cref="Faderio.Core.Playback.PlayerView.EditorFollow"/>).
    /// The transport's, as shuffle is: <c>SetEditorFollowsPlayback</c> saves it.</summary>
    public bool EditorFollowsPlayback { get; init; }

    /// <summary>The playlist open when this library was last used, which the next launch opens
    /// (D20); null before any has been. The engine keeps it: <c>ChangeSettings</c> keeps the one
    /// in force (playlists issue 01).</summary>
    public string? LastPlaylist { get; init; }

    /// <summary>These settings as the engine keeps them: the current schema, N and the blank
    /// duration brought into range, and a choice no version knows replaced by its default. A hand-edited file
    /// and a caller alike get the same settings back.</summary>
    public PlayerSettings Normalised() => this with
    {
        SchemaVersion = CurrentSchemaVersion,
        UnauthoredAdjacency = Compat.IsDefined(UnauthoredAdjacency) ? UnauthoredAdjacency : UnauthoredAdjacency.HardCut,
        DefaultCrossfadeSeconds = Compat.Clamp(DefaultCrossfadeSeconds, MinDefaultCrossfadeSeconds, MaxDefaultCrossfadeSeconds),
        BlankDurationSeconds = Compat.Clamp(BlankDurationSeconds, MinBlankDurationSeconds, MaxBlankDurationSeconds),
        Repeat = Compat.IsDefined(Repeat) ? Repeat : RepeatMode.Off,
        ReplayGainMode = FormerReplayGainEnabled == false ? ReplayGainMode.Off
            : Compat.IsDefined(ReplayGainMode) ? ReplayGainMode : ReplayGainMode.Track,
        FormerReplayGainEnabled = null,
    };
}
