using Faderio.Core.Playback;

namespace Faderio.Core.Domain;

/// <summary>A single audio file known to the player; the unit of metadata and playback.</summary>
public sealed record Track
{
    /// <summary>Only the engine issues a track (ADR 0003), so its length is always the engine's
    /// — never one the shell made up. Tests reach it via InternalsVisibleTo.</summary>
    internal Track() { }

    public required string FilePath { get; init; }
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";

    /// <summary>The album the tags name; empty when they name none (playlists issue 06).</summary>
    public string Album { get; init; } = "";

    /// <summary>The one length a track has (ADR 0010): issued by the add-time probe from the
    /// stream header and proven equal to what decodes.</summary>
    public Length Length { get; init; }

    /// <summary>What the add-time probe read from the header alongside the length: format, sample
    /// rate, bit depth and channels (playlists issue 06). Null only for a track that was never
    /// probed, as the transition manager names a gone file.</summary>
    public AudioShape? Format { get; init; }

    /// <summary>The file could not be probed when the engine issued this track: a path the
    /// transition store names whose file is gone, moved or no longer promised (phase 3 issue 02).
    /// Such a track is its path alone. Its transitions can be removed, not changed. A queued track
    /// is never gone: it was probed when it was added, and a file lost since is unreadable.</summary>
    public bool Gone { get; init; }

    /// <summary>ReplayGain track gain in dB, parsed from the file's tags. Null when untagged.</summary>
    public double? ReplayGainDb { get; init; }

    public override string ToString() => string.IsNullOrEmpty(Title) ? FilePath : Title;
}
