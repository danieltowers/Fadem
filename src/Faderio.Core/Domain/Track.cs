namespace Faderio.Core.Domain;

/// <summary>A single audio file known to the player; the unit of metadata and playback.</summary>
public sealed record Track
{
    /// <summary>Only the engine issues a track (ticket 04), so its length is always the
    /// engine's — never one the shell made up. Tests reach it via InternalsVisibleTo.</summary>
    internal Track() { }

    public required string FilePath { get; init; }
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public TimeSpan Duration { get; init; }

    /// <summary>ReplayGain track gain in dB, parsed from the file's tags. Null when untagged.</summary>
    public double? ReplayGainDb { get; init; }

    public override string ToString() => string.IsNullOrEmpty(Title) ? FilePath : Title;
}
